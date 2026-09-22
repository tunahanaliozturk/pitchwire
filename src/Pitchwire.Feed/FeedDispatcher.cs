using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pitchwire.Contracts;

namespace Pitchwire.Feed;

/// <summary>
/// What the provider did to a match on its way out, counted.
/// </summary>
/// <remarks>
/// Returned so a caller can prove the run actually misbehaved. A test that turns the rates up and
/// never checks that anything was dropped would keep passing after the rates were set to zero, and it
/// would then be testing the happy path under a name that says otherwise.
/// </remarks>
public sealed record FeedRunStatistics(int Delivered, int Dropped, int Duplicated, int Reordered);

/// <summary>
/// Plays a match out over the wire, with all the bad habits of a real provider.
/// </summary>
public sealed partial class FeedDispatcher(
    HttpClient client,
    IOptions<FeedOptions> options,
    FeedLedger ledger,
    TimeProvider clock,
    ILogger<FeedDispatcher> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FeedRunStatistics> PlayAsync(CatalogueFixture fixture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var settings = options.Value;
        var plan = MatchScript.For(fixture, settings.Seed);
        var script = plan.Events;

        // The team sheets go out before the first whistle, the way a provider publishes them, and
        // again at half time. They are snapshots, so a second copy costs nothing and a lost first one
        // needs nobody to ask for it.
        var pendingLineups = new List<LineupPayload>(plan.Lineups);
        var pendingStatistics = new List<StatisticsPayload>();
        var nextSnapshot = 0;

        var rolls = new Rolls(settings.Seed ^ (ulong)fixture.Id.GetHashCode() ^ 0xD1B54A32D192ED03);
        var pending = new List<MatchEventPayload>();
        var held = new List<MatchEventPayload>();
        var previousMinute = 0;
        var delivered = 0;
        var dropped = 0;
        var duplicated = 0;
        var reordered = 0;

        foreach (var @event in script)
        {
            await WaitForMinuteAsync(@event.Minute - previousMinute, settings, cancellationToken);
            previousMinute = @event.Minute;

            // Statistics are cumulative, so the ones whose minute has passed travel with whatever
            // goes next. A snapshot nobody delivers is replaced by the following one.
            while (nextSnapshot < plan.Statistics.Count && plan.Statistics[nextSnapshot].AsOfMinute <= @event.Minute)
            {
                pendingStatistics.Add(plan.Statistics[nextSnapshot]);
                nextSnapshot++;
            }

            if (@event.Kind == MatchEventKind.PeriodStart && @event.Minute > 0)
            {
                pendingLineups.AddRange(plan.Lineups);
            }

            // Noted as it happens, before the decision to send it. The provider knows about a goal it
            // then failed to deliver, and it must not know about one that has not been scored yet.
            ledger.Note(fixture.Id, @event);

            if (rolls.Chance(settings.DropRate))
            {
                // Never sent. The API will notice the hole in the sequence and ask for it.
                Dropped(logger, fixture.Id, @event.Sequence);
                dropped++;
                continue;
            }

            if (rolls.Chance(settings.ReorderRate))
            {
                // Held back so the next event overtakes it, which is what two workers racing looks
                // like from the outside.
                held.Add(@event);
                reordered++;
                continue;
            }

            pending.Add(@event);

            if (held.Count > 0)
            {
                pending.AddRange(held);
                held.Clear();
            }

            if (rolls.Chance(settings.DuplicateRate))
            {
                // A retry that crossed with an acknowledgement.
                pending.Add(@event);
                duplicated++;
            }

            if (rolls.Chance(settings.BurstPauseRate))
            {
                await WaitForMinuteAsync(settings.BurstPauseMinutes, settings, cancellationToken);
                continue;
            }

            delivered += pending.Count;
            await SendAsync(pending, pendingLineups, pendingStatistics, settings, cancellationToken);
            pending.Clear();
            pendingLineups.Clear();
            pendingStatistics.Clear();
        }

        pending.AddRange(held);
        pendingStatistics.AddRange(plan.Statistics.Skip(nextSnapshot));

        if (pending.Count > 0 || pendingStatistics.Count > 0 || pendingLineups.Count > 0)
        {
            delivered += pending.Count;
            await SendAsync(pending, pendingLineups, pendingStatistics, settings, cancellationToken);
        }

        return new FeedRunStatistics(delivered, dropped, duplicated, reordered);
    }

    private async Task SendAsync(
        List<MatchEventPayload> batch,
        List<LineupPayload> lineups,
        List<StatisticsPayload> statistics,
        FeedOptions settings,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0 && lineups.Count == 0 && statistics.Count == 0)
        {
            return;
        }

        var stamp = clock.GetUtcNow();
        var body = JsonSerializer.SerializeToUtf8Bytes(new IngestRequest([.. batch], [.. lineups], [.. statistics]), Json);

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "ingest/events") { Content = content };
        request.Headers.Add("X-Pitchwire-Signature", RequestSigner.Sign(settings.Secret, stamp, body));
        request.Headers.Add(
            "X-Pitchwire-Timestamp",
            stamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            DeliveryRefused(logger, (int)response.StatusCode, batch.Count);
            return;
        }

        var outcome = await response.Content.ReadFromJsonAsync<IngestResponse>(Json, cancellationToken);

        if (outcome is not null && outcome.Rejected > 0)
        {
            // Worth saying out loud. A provider that keeps sending events nobody can apply is sending
            // them for a fixture the consumer does not have.
            EventsRejected(logger, outcome.Rejected);
        }
    }

    private async Task WaitForMinuteAsync(int matchMinutes, FeedOptions settings, CancellationToken cancellationToken)
    {
        if (matchMinutes <= 0)
        {
            return;
        }

        var realSeconds = matchMinutes * 60 / settings.ClockFactor;

        if (realSeconds < 0.001)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(realSeconds), clock, cancellationToken);
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug,
        Message = "Dropped event {Sequence} of match {MatchId} on purpose.")]
    private static partial void Dropped(ILogger logger, Guid matchId, int sequence);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Delivery of {Count} events was refused with status {StatusCode}.")]
    private static partial void DeliveryRefused(ILogger logger, int statusCode, int count);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "The consumer rejected {Count} events.")]
    private static partial void EventsRejected(ILogger logger, int count);
}
