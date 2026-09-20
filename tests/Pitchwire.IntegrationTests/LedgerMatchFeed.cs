using Pitchwire.Application.Ingestion;
using Pitchwire.Contracts;
using Pitchwire.Feed;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// The provider's repair endpoint, in process.
/// </summary>
/// <remarks>
/// It answers from the same ledger the HTTP endpoint serves, so the end to end test exercises the
/// real recovery path without a second web host in the way.
/// </remarks>
public sealed class LedgerMatchFeed(FeedLedger ledger) : IMatchFeed
{
    public Task<IReadOnlyList<MatchEventPayload>> GetEventsAsync(
        Guid matchId,
        int fromSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return Task.FromResult(ledger.From(matchId, fromSequence));
    }
}
