using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pitchwire.Application.Persistence;
using Pitchwire.Application.Telemetry;
using Pitchwire.Domain;

namespace Pitchwire.Application.Ingestion;

/// <summary>
/// Asks the provider about matches that have gone quiet.
/// </summary>
/// <remarks>
/// Gap repair only triggers when a later event reveals a hole, so it cannot see the one case where
/// nothing later ever arrives: the provider dropping the final whistle. That match would sit at
/// eighty seven minutes with the wrong score for the rest of the season. This is the sweep that
/// notices, and it is the reason a match row records when it last heard anything.
/// </remarks>
public sealed partial class StaleMatchSweeper(
    IPitchwireDbContext db,
    IMatchFeed feed,
    EventIngestor ingestor,
    IOptions<IngestOptions> options,
    TimeProvider clock,
    IngestionMetrics metrics,
    ILogger<StaleMatchSweeper> logger)
{
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var quietSince = clock.GetUtcNow() - settings.StaleAfter;

        var quiet = await db.Matches
            .AsNoTracking()
            .Where(m => (m.Status == MatchStatus.Live || m.Status == MatchStatus.Halftime)
                && (m.LastEventAt == null || m.LastEventAt < quietSince))
            .Select(m => new { m.Id, m.LastEventSequence })
            .ToListAsync(cancellationToken);

        var recovered = 0;

        foreach (var match in quiet)
        {
            var missing = await feed.GetEventsAsync(match.Id, match.LastEventSequence + 1, cancellationToken);

            if (missing.Count == 0)
            {
                continue;
            }

            var outcome = await ingestor.IngestAsync(settings.Provider, missing, cancellationToken);
            recovered += outcome.Accepted;

            if (outcome.Accepted > 0)
            {
                SweptMatch(logger, match.Id, outcome.Accepted);
                metrics.Repaired(outcome.Accepted);
            }
        }

        return recovered;
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information,
        Message = "A quiet match {MatchId} gave up {Accepted} events the provider never pushed.")]
    private static partial void SweptMatch(ILogger logger, Guid matchId, int accepted);
}
