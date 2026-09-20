using System.Net.Http.Json;
using Pitchwire.Application.Ingestion;
using Pitchwire.Contracts;

namespace Pitchwire.Infrastructure.Ingestion;

/// <summary>
/// Asks the provider over HTTP for the events it did not manage to push.
/// </summary>
internal sealed class HttpMatchFeed(HttpClient client) : IMatchFeed
{
    public async Task<IReadOnlyList<MatchEventPayload>> GetEventsAsync(
        Guid matchId,
        int fromSequence,
        CancellationToken cancellationToken)
    {
        var answer = await client.GetFromJsonAsync<List<MatchEventPayload>>(
            $"matches/{matchId}/events?from={fromSequence}",
            cancellationToken);

        return answer ?? [];
    }
}
