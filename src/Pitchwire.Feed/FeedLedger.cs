using System.Collections.Concurrent;
using Pitchwire.Contracts;

namespace Pitchwire.Feed;

/// <summary>
/// What the provider believes happened, including the events it failed to deliver.
/// </summary>
/// <remarks>
/// This is the provider's own record, which is why the repair endpoint can serve from it: an event
/// that was dropped on the way out still exists here, and that is exactly what makes a gap
/// recoverable at all.
/// </remarks>
public sealed class FeedLedger
{
    private readonly ConcurrentDictionary<Guid, MatchEventPayload[]> _scripts = new();

    public void Record(Guid matchId, IReadOnlyList<MatchEventPayload> script) =>
        _scripts[matchId] = [.. script];

    public IReadOnlyList<MatchEventPayload> From(Guid matchId, int fromSequence) =>
        _scripts.TryGetValue(matchId, out var script)
            ? [.. script.Where(e => e.Sequence >= fromSequence)]
            : [];

    /// <summary>
    /// The score according to the provider.
    /// </summary>
    /// <remarks>
    /// Counted here rather than through the consumer's reducer on purpose. The end to end test
    /// compares this number with the one the API derived, and a comparison of two runs of the same
    /// code would only prove that the code is consistent with itself.
    /// </remarks>
    public (int Home, int Away) ScoreFor(Guid matchId, Guid homeTeamId)
    {
        var home = 0;
        var away = 0;

        foreach (var @event in From(matchId, 1))
        {
            var byHome = @event.TeamId == homeTeamId;

            switch (@event.Kind)
            {
                case MatchEventKind.Goal:
                case MatchEventKind.PenaltyGoal:
                    if (byHome)
                    {
                        home++;
                    }
                    else
                    {
                        away++;
                    }

                    break;

                case MatchEventKind.OwnGoal:
                    if (byHome)
                    {
                        away++;
                    }
                    else
                    {
                        home++;
                    }

                    break;

                default:
                    break;
            }
        }

        return (home, away);
    }
}
