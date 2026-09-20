using Pitchwire.Contracts;

namespace Pitchwire.Api.Domain;

/// <summary>
/// One event exactly as it arrived. Append only, and the source of truth for everything derived.
/// </summary>
/// <remarks>
/// A late or duplicated delivery is stored too. The log records what the provider said and when it
/// said it, which is the only way to answer afterwards why a score was what it was.
/// </remarks>
public sealed class MatchEvent
{
    public Guid Id { get; init; }

    public Guid MatchId { get; set; }

    /// <summary>
    /// Which provider sent it. Part of the uniqueness key, because two providers are free to reuse
    /// each other's identifiers and nothing stops them.
    /// </summary>
    public required string Provider { get; set; }

    public required string ProviderEventId { get; set; }

    public int Sequence { get; set; }

    public int Minute { get; set; }

    public MatchEventKind Kind { get; set; }

    public Guid TeamId { get; set; }

    public Guid? PlayerId { get; set; }

    public Guid? AssistPlayerId { get; set; }

    /// <summary>When the provider says it happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// When it reached this service. The gap between the two is the delivery lag, and it is the number
    /// the goal to screen measurement is built on.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public Match? Match { get; set; }
}
