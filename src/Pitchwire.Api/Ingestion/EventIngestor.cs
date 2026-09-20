using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pitchwire.Api.Domain;
using Pitchwire.Api.Persistence;
using Pitchwire.Contracts;

namespace Pitchwire.Api.Ingestion;

/// <summary>
/// Takes a batch from the provider and turns it into stored events and an updated match.
/// </summary>
/// <remarks>
/// Everything a batch does happens in one transaction: the events, the match row and, later, the
/// notification outbox. Broadcasting or invalidating anything before that transaction commits would
/// put a goal on screens that a rollback then takes back, and a goal cannot be taken back.
/// </remarks>
public sealed class EventIngestor(PitchwireDbContext db, TimeProvider clock)
{
    private const string UniqueViolation = "23505";

    public async Task<IngestResponse> IngestAsync(
        string provider,
        IReadOnlyList<MatchEventPayload> events,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return new IngestResponse(0, 0, 0);
        }

        var accepted = 0;
        var duplicate = 0;
        var rejected = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var group in events.GroupBy(e => e.MatchId))
        {
            // One match at a time, under a lock on that match's row. Events for a single match have to
            // be applied in series or two concurrent batches can both read the same score and both add
            // one to it. Locking the row rather than the table leaves every other match ingesting in
            // parallel, which matters on a Saturday afternoon.
            var match = await db.Matches
                .FromSql($"SELECT * FROM matches WHERE id = {group.Key} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (match is null)
            {
                // A fixture this service does not have. Counted rather than swallowed, so the provider
                // can see that it is sending events nobody can apply.
                rejected += group.Count();
                continue;
            }

            foreach (var payload in group.OrderBy(e => e.Sequence))
            {
                var stored = new MatchEvent
                {
                    Id = Guid.CreateVersion7(),
                    MatchId = match.Id,
                    Provider = provider,
                    ProviderEventId = payload.ProviderEventId,
                    Sequence = payload.Sequence,
                    Minute = payload.Minute,
                    Kind = payload.Kind,
                    TeamId = payload.TeamId,
                    PlayerId = payload.PlayerId,
                    AssistPlayerId = payload.AssistPlayerId,
                    OccurredAt = payload.OccurredAt,
                    ReceivedAt = clock.GetUtcNow(),
                };

                db.MatchEvents.Add(stored);

                try
                {
                    // Saved one event at a time so a redelivery inside a batch costs only that event.
                    // EF Core takes a savepoint per call while a transaction is open, so a violation
                    // rolls back the one insert and leaves the rest of the batch standing. If ingestion
                    // throughput ever becomes the bottleneck, the upgrade is a single insert with
                    // ON CONFLICT DO NOTHING, measured before it is written.
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException failure) when (IsDuplicate(failure))
                {
                    // Expected. A provider that retries is a provider doing its job.
                    duplicate++;
                    db.Entry(stored).State = EntityState.Detached;
                    continue;
                }

                MatchStateReducer.Apply(match, stored);
                accepted++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new IngestResponse(accepted, duplicate, rejected);
    }

    private static bool IsDuplicate(DbUpdateException failure) =>
        failure.InnerException is PostgresException { SqlState: UniqueViolation };
}
