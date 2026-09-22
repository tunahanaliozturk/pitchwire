using Pitchwire.Domain;

namespace Pitchwire.Application.Reads;

/// <summary>
/// One named player, with what the log says they did and what that adds up to.
/// </summary>
public sealed record LineupPlayerView(
    Guid PlayerId,
    string Player,
    int ShirtNumber,
    string Position,
    bool Starter,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int YellowCards,
    int RedCards,
    decimal? Rating);

public sealed record TeamSheetView(Guid TeamId, string Formation, IReadOnlyList<LineupPlayerView> Players);

public sealed record TeamStatisticsView(
    Guid TeamId,
    int AsOfMinute,
    int Possession,
    int Shots,
    int ShotsOnTarget,
    int Corners,
    int Fouls,
    int Offsides);

/// <summary>
/// Turns a team sheet and an event log into the rows a match page shows.
/// </summary>
/// <remarks>
/// Everything here is derived. Minutes come from the substitutions and dismissals in the log, the
/// goals and cards are counted from the same log, and the rating is a function of those two. Nothing
/// is stored that could disagree with the events, which is the only way a page and a timeline can be
/// guaranteed to tell the same story.
/// </remarks>
public static class MatchSheetAssembly
{
    public static IReadOnlyList<TeamSheetView> Build(
        Match match,
        IReadOnlyList<MatchTeamSheet> sheets,
        IReadOnlyList<MatchLineupEntry> lineup,
        IReadOnlyList<MatchEvent> events,
        IReadOnlyDictionary<Guid, string> names)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(lineup);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(names);

        var minutes = Participation.MinutesPlayed(lineup, events, match.Minute);

        // One pass over the log rather than one pass per player. With eighteen names a side and a
        // busy match that is the difference between a few hundred comparisons and several thousand,
        // and it was worth doing because a benchmark said so rather than because it reads better.
        var tallies = Count(events);

        return
        [
            .. lineup
                .GroupBy(entry => entry.TeamId)
                .Select(side => new TeamSheetView(
                    side.Key,
                    sheets.FirstOrDefault(sheet => sheet.TeamId == side.Key)?.Formation ?? "",
                    [
                        .. side
                            .OrderByDescending(entry => entry.IsStarter)
                            .ThenBy(entry => entry.Position)
                            .ThenBy(entry => entry.ShirtNumber)
                            .Select(entry => ToView(match, entry, tallies, minutes, names)),
                    ])),
        ];
    }

    /// <summary>
    /// What each named player did, counted once.
    /// </summary>
    private readonly record struct Tally(int Goals, int Penalties, int OwnGoals, int Assists, int Yellows, int Reds);

    private static Dictionary<Guid, Tally> Count(IReadOnlyList<MatchEvent> events)
    {
        var tallies = new Dictionary<Guid, Tally>();

        foreach (var @event in events)
        {
            if (@event.PlayerId is { } player)
            {
                var current = tallies.GetValueOrDefault(player);

                tallies[player] = @event.Kind switch
                {
                    MatchEventKind.Goal => current with { Goals = current.Goals + 1 },
                    MatchEventKind.PenaltyGoal => current with
                    {
                        Goals = current.Goals + 1,
                        Penalties = current.Penalties + 1,
                    },
                    MatchEventKind.OwnGoal => current with { OwnGoals = current.OwnGoals + 1 },
                    MatchEventKind.Yellow => current with { Yellows = current.Yellows + 1 },
                    MatchEventKind.Red => current with { Reds = current.Reds + 1 },
                    _ => current,
                };
            }

            if (@event.AssistPlayerId is { } helper)
            {
                var current = tallies.GetValueOrDefault(helper);
                tallies[helper] = current with { Assists = current.Assists + 1 };
            }
        }

        return tallies;
    }

    private static LineupPlayerView ToView(
        Match match,
        MatchLineupEntry entry,
        Dictionary<Guid, Tally> tallies,
        IReadOnlyDictionary<Guid, int> minutes,
        IReadOnlyDictionary<Guid, string> names)
    {
        var (goals, penalties, ownGoals, assists, yellows, reds) = tallies.GetValueOrDefault(entry.PlayerId);

        var forHome = entry.TeamId == match.HomeTeamId;
        var played = minutes.TryGetValue(entry.PlayerId, out var onPitch) ? onPitch : 0;

        var rating = PlayerRating.Calculate(new PlayerMatchLine(
            entry.PlayerId,
            entry.Position,
            played,
            goals,
            penalties,
            ownGoals,
            assists,
            yellows,
            reds,
            forHome ? match.HomeScore : match.AwayScore,
            forHome ? match.AwayScore : match.HomeScore));

        return new LineupPlayerView(
            entry.PlayerId,
            names.TryGetValue(entry.PlayerId, out var name) ? name : "Unknown",
            entry.ShirtNumber,
            entry.Position.ToString(),
            entry.IsStarter,
            played,
            goals,
            assists,
            yellows,
            reds,
            rating);
    }
}
