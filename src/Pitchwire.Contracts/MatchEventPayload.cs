namespace Pitchwire.Contracts;

/// <summary>
/// One event as the provider reports it.
/// </summary>
/// <param name="ProviderEventId">
/// The provider's own identifier for this event. Paired with the provider name it is what makes a
/// redelivery recognisable, so it is the one field a provider must never reuse for a second event.
/// </param>
/// <param name="Sequence">
/// Position within the match, starting at one. The provider promises the numbering, not the delivery
/// order, which is why the ingestion boundary compares rather than trusts.
/// </param>
/// <param name="Minute">
/// Match minute as the provider reports it. Server time never derives this: it knows nothing about
/// half time, stoppage time or a suspended match.
/// </param>
public sealed record MatchEventPayload(
    string ProviderEventId,
    Guid MatchId,
    int Sequence,
    int Minute,
    MatchEventKind Kind,
    Guid TeamId,
    Guid? PlayerId,
    Guid? AssistPlayerId,
    DateTimeOffset OccurredAt);
