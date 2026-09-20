using System.Collections.Concurrent;
using Pitchwire.Contracts;

namespace Pitchwire.Feed;

/// <summary>
/// What the provider believes has happened so far, including the events it failed to deliver.
/// </summary>
/// <remarks>
/// An event is noted the moment the match reaches it, not when the script is written. A provider
/// cannot hand back a goal that has not been scored yet, and a ledger holding the whole match in
/// advance would let a consumer asking for a missing event fast forward to the final whistle. That is
/// exactly what it did before this was fixed, and the symptom was matches ending in half the time
/// they should take.
/// <para>
/// A dropped event is still noted here. The ledger is the provider's own record, and a delivery that
/// failed is what makes a gap recoverable at all.
/// </para>
/// </remarks>
public sealed class FeedLedger
{
    private readonly ConcurrentDictionary<Guid, List<MatchEventPayload>> _known = new();

    public void Note(Guid matchId, MatchEventPayload @event)
    {
        var events = _known.GetOrAdd(matchId, _ => []);

        lock (events)
        {
            events.Add(@event);
        }
    }

    public IReadOnlyList<MatchEventPayload> From(Guid matchId, int fromSequence)
    {
        if (!_known.TryGetValue(matchId, out var events))
        {
            return [];
        }

        lock (events)
        {
            return [.. events.Where(e => e.Sequence >= fromSequence).OrderBy(e => e.Sequence)];
        }
    }

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
