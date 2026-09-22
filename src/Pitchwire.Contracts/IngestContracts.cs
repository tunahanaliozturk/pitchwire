namespace Pitchwire.Contracts;

/// <summary>
/// A batch of events. Providers send batches because they retry, buffer and catch up, so a single
/// event endpoint would only mean the same batch arriving one request at a time.
/// </summary>
/// <remarks>
/// Team sheets and statistics travel in the same signed batch as events rather than through
/// endpoints of their own. One boundary, one signature and one replay window is easier to get right
/// than three, and a provider catching up after a pause sends all three kinds at once anyway.
/// </remarks>
public sealed record IngestRequest(
    IReadOnlyList<MatchEventPayload> Events,
    IReadOnlyList<LineupPayload>? Lineups = null,
    IReadOnlyList<StatisticsPayload>? Statistics = null);

/// <summary>
/// What the boundary did with a batch.
/// </summary>
/// <remarks>
/// The counts are returned rather than kept quiet so the provider can log them. A duplicate is a
/// normal outcome and a rejection is not, and a caller that cannot tell the two apart will keep
/// sending the rejected event forever.
/// </remarks>
public sealed record IngestResponse(
    int Accepted,
    int Duplicate,
    int Rejected,
    int LineupsStored = 0,
    int StatisticsStored = 0);
