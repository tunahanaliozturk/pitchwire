using Pitchwire.Contracts;

namespace Pitchwire.Application.Ingestion;

/// <summary>
/// The provider, as this service needs it.
/// </summary>
/// <remarks>
/// A push feed can only be trusted to tell you what it remembers to send. This is the other half:
/// the way to ask for a range it missed. It is also the seam a real provider arrives through, so
/// swapping the simulator for one is a matter of another implementation and a configuration change.
/// </remarks>
public interface IMatchFeed
{
    Task<IReadOnlyList<MatchEventPayload>> GetEventsAsync(
        Guid matchId,
        int fromSequence,
        CancellationToken cancellationToken);
}

/// <summary>
/// A hole in one match's event sequence, and where it starts.
/// </summary>
public sealed record GapRepairRequest(Guid MatchId, int FromSequence);
