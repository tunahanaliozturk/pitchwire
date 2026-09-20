
namespace Pitchwire.Domain;

/// <summary>
/// The totals a match carries after a run of events.
/// </summary>
public readonly record struct MatchTotals(
    int HomeScore,
    int AwayScore,
    int Minute,
    MatchStatus Status,
    int LastEventSequence);

/// <summary>
/// Turns match events into the score, minute and status shown to a reader.
/// </summary>
/// <remarks>
/// Pure, and deliberately free of any database concern, because this is the part that has to be
/// obviously right. A wrong score is the one defect a user notices at once and cannot work around.
/// </remarks>
public static class MatchStateReducer
{
    /// <summary>
    /// A period ending before this minute is half time, and one ending later is full time.
    /// </summary>
    /// <remarks>
    /// League football is two periods, and the gap between a first half with stoppage time, around 50,
    /// and the end of a second half, around 90, is wide enough that one threshold separates them. Cup
    /// matches with extra time would need the provider to number the period on the event instead, and
    /// that is the change to make when those arrive rather than now.
    /// </remarks>
    private const int SecondHalfMinute = 60;

    public static void Apply(Match match, MatchEvent @event)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(@event);

        var scoredByHome = @event.TeamId == match.HomeTeamId;

        switch (@event.Kind)
        {
            case MatchEventKind.Goal:
            case MatchEventKind.PenaltyGoal:
                AddGoal(match, toHome: scoredByHome);
                break;

            case MatchEventKind.OwnGoal:
                // The event names the team that put the ball into its own net, so the goal belongs to
                // the other side.
                AddGoal(match, toHome: !scoredByHome);
                break;

            case MatchEventKind.PeriodStart:
                match.Status = MatchStatus.Live;
                break;

            case MatchEventKind.PeriodEnd:
                match.Status = @event.Minute < SecondHalfMinute ? MatchStatus.Halftime : MatchStatus.Finished;
                break;

            case MatchEventKind.Yellow:
            case MatchEventKind.Red:
            case MatchEventKind.Substitution:
                // Recorded in the event log and shown on the timeline, but they move no number here.
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(@event), @event.Kind, "Unknown match event kind.");
        }

        match.Minute = @event.Minute;
        match.LastEventSequence = @event.Sequence;
    }

    /// <summary>
    /// Rebuilds the totals from a match's whole event log, in sequence order.
    /// </summary>
    /// <remarks>
    /// This is what a late delivery triggers. Rebuilding can only add the event that was missing,
    /// where reversing the last change needs a correct inverse for every event type, and one sign
    /// error there becomes a wrong score with nothing in the data explaining where it came from.
    /// </remarks>
    public static MatchTotals Recompute(Match match, IReadOnlyList<MatchEvent> events)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(events);

        var rebuilt = new Match
        {
            Id = match.Id,
            SeasonId = match.SeasonId,
            HomeTeamId = match.HomeTeamId,
            AwayTeamId = match.AwayTeamId,
            KickoffUtc = match.KickoffUtc,
            Round = match.Round,
            Status = MatchStatus.Scheduled,
        };

        foreach (var @event in events.OrderBy(e => e.Sequence))
        {
            Apply(rebuilt, @event);
        }

        return new MatchTotals(
            rebuilt.HomeScore,
            rebuilt.AwayScore,
            rebuilt.Minute,
            rebuilt.Status,
            rebuilt.LastEventSequence);
    }

    private static void AddGoal(Match match, bool toHome)
    {
        if (toHome)
        {
            match.HomeScore++;
        }
        else
        {
            match.AwayScore++;
        }
    }
}
