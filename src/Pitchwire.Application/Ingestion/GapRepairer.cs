using Microsoft.Extensions.Options;
using Pitchwire.Application.Telemetry;

namespace Pitchwire.Application.Ingestion;

/// <summary>
/// Closes one hole in a match's event sequence by asking the provider for the range that is missing.
/// </summary>
/// <remarks>
/// Separate from the hosted service that drains the queue, so the repair can be exercised directly
/// rather than through a background loop and a timeout.
/// </remarks>
public sealed partial class GapRepairer(
    IMatchFeed feed,
    EventIngestor ingestor,
    IOptions<IngestOptions> options,
    IngestionMetrics metrics,
    ILogger<GapRepairer> logger)
{
    public async Task RepairAsync(GapRepairRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var missing = await feed.GetEventsAsync(request.MatchId, request.FromSequence, cancellationToken);

        if (missing.Count == 0)
        {
            // The mark stays. Clearing it here would claim the score is complete while the hole is
            // still there, which is the one thing worse than showing a match as degraded.
            RepairFoundNothing(logger, request.MatchId, request.FromSequence);
            return;
        }

        var outcome = await ingestor.IngestAsync(options.Value.Provider, missing, cancellationToken);
        metrics.Repaired(outcome.Accepted);
        RepairFinished(logger, request.MatchId, outcome.Accepted, outcome.Duplicate);
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Warning,
        Message = "Repair for match {MatchId} from sequence {FromSequence} came back empty.")]
    private static partial void RepairFoundNothing(ILogger logger, Guid matchId, int fromSequence);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "Repair for match {MatchId} stored {Accepted} events and skipped {Duplicate} already known.")]
    private static partial void RepairFinished(ILogger logger, Guid matchId, int accepted, int duplicate);
}
