using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Persistence;
using Pitchwire.Application.Telemetry;
using Pitchwire.Contracts;
using Pitchwire.Domain;

namespace Pitchwire.Application.Ingestion;

/// <summary>
/// Takes a batch from the provider and turns it into stored events and an updated match.
/// </summary>
/// <remarks>
/// Everything a batch does happens in one transaction: the events, the match row and, later, the
/// notification outbox. Broadcasting or invalidating anything before that transaction commits would
/// put a goal on screens that a rollback then takes back, and a goal cannot be taken back.
/// </remarks>
public sealed class EventIngestor(
    IPitchwireDbContext db,
    IStoreFailures failures,
    TimeProvider clock,
    GapRepairBacklog repairs,
    IngestionMetrics metrics)
{
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
        var pendingRepairs = new List<GapRepairRequest>();

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

            var outcome = await ApplyGroupAsync(provider, match, group, cancellationToken);
            accepted += outcome.Accepted;
            duplicate += outcome.Duplicate;
            rejected += outcome.Rejected;

            if (outcome.GapFrom is { } gapFrom)
            {
                pendingRepairs.Add(new GapRepairRequest(match.Id, gapFrom));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        metrics.Accepted(accepted);
        metrics.Duplicate(duplicate);
        metrics.Rejected(rejected);

        // Queued after the commit. A repair that started while this transaction was open would read
        // the state from before it and ask for a range that is no longer missing.
        foreach (var repair in pendingRepairs)
        {
            repairs.TryEnqueue(repair);
        }

        return new IngestResponse(accepted, duplicate, rejected);
    }

    private async Task<GroupOutcome> ApplyGroupAsync(
        string provider,
        Match match,
        IEnumerable<MatchEventPayload> payloads,
        CancellationToken cancellationToken)
    {
        var accepted = 0;
        var duplicate = 0;
        var rejected = 0;
        int? gapFrom = null;
        var rebuildNeeded = false;

        foreach (var payload in payloads.OrderBy(e => e.Sequence))
        {
            if (!MatchEventTranslation.TryToDomain(payload.Kind, out var kind))
            {
                // A kind this system has no rule for. Storing it would put a value in the log that no
                // reducer can read, and every rebuild from that point on would have to guess.
                rejected++;
                continue;
            }

            var stored = ToEntity(provider, match.Id, payload, kind);
            db.MatchEvents.Add(stored);

            try
            {
                // Saved one event at a time so a redelivery inside a batch costs only that event. EF
                // Core takes a savepoint per call while a transaction is open, so a violation rolls
                // back the one insert and leaves the rest of the batch standing. If ingestion
                // throughput ever becomes the bottleneck, the upgrade is a single insert with
                // ON CONFLICT DO NOTHING, measured before it is written.
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException failure) when (failures.IsUniqueViolation(failure))
            {
                // Expected. A provider that retries is a provider doing its job.
                duplicate++;
                db.Entry(stored).State = EntityState.Detached;
                continue;
            }

            accepted++;
            metrics.RecordDeliveryLag(stored.ReceivedAt - stored.OccurredAt);

            if (payload.Sequence <= match.LastEventSequence)
            {
                // An event that turned up after the match moved past it. The score is rebuilt from the
                // log rather than adjusted, because an adjustment needs a correct inverse for every
                // event type and a sign error there is unrecoverable.
                rebuildNeeded = true;
                match.LastEventAt = stored.ReceivedAt;
                continue;
            }

            if (payload.Sequence > match.LastEventSequence + 1)
            {
                // Numbers were skipped. The event still applies, and what is missing is asked for.
                gapFrom ??= match.LastEventSequence + 1;
            }

            MatchStateReducer.Apply(match, stored);
            match.LastEventAt = stored.ReceivedAt;
        }

        if (rebuildNeeded)
        {
            await RebuildAsync(provider, match, cancellationToken);
        }

        // The mark is only worth trusting if it is checked whenever it could have changed. A match
        // that arrived degraded is checked even when this batch changed nothing, because the event
        // that closed the hole may have been delivered twice and counted as a duplicate here.
        if (rebuildNeeded || gapFrom is not null || match.IsDegraded)
        {
            match.IsDegraded = !await IsContiguousAsync(provider, match, cancellationToken);
        }

        return new GroupOutcome(accepted, duplicate, rejected, gapFrom);
    }

    private async Task RebuildAsync(string provider, Match match, CancellationToken cancellationToken)
    {
        var known = await db.MatchEvents
            .Where(e => e.MatchId == match.Id && e.Provider == provider)
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken);

        var totals = MatchStateReducer.Recompute(match, known);

        match.HomeScore = totals.HomeScore;
        match.AwayScore = totals.AwayScore;
        match.Minute = totals.Minute;
        match.Status = totals.Status;
        match.LastEventSequence = totals.LastEventSequence;
    }

    private async Task<bool> IsContiguousAsync(string provider, Match match, CancellationToken cancellationToken)
    {
        // Sequences start at one, so a complete log holds exactly as many distinct numbers as the
        // highest one applied. Counting is cheaper than fetching them to look for the hole.
        var distinct = await db.MatchEvents
            .Where(e => e.MatchId == match.Id && e.Provider == provider)
            .Select(e => e.Sequence)
            .Distinct()
            .CountAsync(cancellationToken);

        return distinct == match.LastEventSequence;
    }

    private MatchEvent ToEntity(string provider, Guid matchId, MatchEventPayload payload, Domain.MatchEventKind kind) => new()
    {
        Id = Guid.CreateVersion7(),
        MatchId = matchId,
        Provider = provider,
        ProviderEventId = payload.ProviderEventId,
        Sequence = payload.Sequence,
        Minute = payload.Minute,
        Kind = kind,
        TeamId = payload.TeamId,
        PlayerId = payload.PlayerId,
        AssistPlayerId = payload.AssistPlayerId,
        OccurredAt = payload.OccurredAt,
        ReceivedAt = clock.GetUtcNow(),
    };

    private readonly record struct GroupOutcome(int Accepted, int Duplicate, int Rejected, int? GapFrom);
}
