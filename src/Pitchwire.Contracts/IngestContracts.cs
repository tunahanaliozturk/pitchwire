namespace Pitchwire.Contracts;

/// <summary>
/// A batch of events. Providers send batches because they retry, buffer and catch up, so a single
/// event endpoint would only mean the same batch arriving one request at a time.
/// </summary>
public sealed record IngestRequest(IReadOnlyList<MatchEventPayload> Events);

/// <summary>
/// What the boundary did with a batch.
/// </summary>
/// <remarks>
/// The counts are returned rather than kept quiet so the provider can log them. A duplicate is a
/// normal outcome and a rejection is not, and a caller that cannot tell the two apart will keep
/// sending the rejected event forever.
/// </remarks>
public sealed record IngestResponse(int Accepted, int Duplicate, int Rejected);
