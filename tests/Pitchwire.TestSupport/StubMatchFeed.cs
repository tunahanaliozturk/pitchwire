using System.Collections.Concurrent;
using Pitchwire.Application.Ingestion;
using Pitchwire.Contracts;

namespace Pitchwire.TestSupport;

/// <summary>
/// A provider whose answers the test decides, and which records what it was asked for.
/// </summary>
/// <remarks>
/// The interesting assertion in gap repair is not only that the hole closed, but that the service
/// asked for the right range. A feed that is asked from sequence one every time would still pass a
/// test that only looked at the final score, while refetching a whole match on every hiccup.
/// </remarks>
public sealed class StubMatchFeed(params MatchEventPayload[] events) : IMatchFeed
{
    private readonly ConcurrentQueue<(Guid MatchId, int FromSequence)> _requests = new();

    public IReadOnlyCollection<(Guid MatchId, int FromSequence)> Requests => _requests;

    public Task<IReadOnlyList<MatchEventPayload>> GetEventsAsync(
        Guid matchId,
        int fromSequence,
        CancellationToken cancellationToken)
    {
        _requests.Enqueue((matchId, fromSequence));

        IReadOnlyList<MatchEventPayload> answer =
        [
            .. events.Where(e => e.MatchId == matchId && e.Sequence >= fromSequence)
        ];

        return Task.FromResult(answer);
    }
}
