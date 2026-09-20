using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Persistence;
using Pitchwire.Domain;

namespace Pitchwire.Application.Notifications;

/// <summary>
/// What the push service said about one attempt.
/// </summary>
public enum PushOutcome
{
    Delivered,

    /// <summary>Something temporary. Worth another try, later.</summary>
    Retry,

    /// <summary>The subscription is finished. Retrying it forever would be a slow leak.</summary>
    Gone,
}

/// <summary>
/// Where a notification actually goes.
/// </summary>
/// <remarks>
/// A port, so the delivery rules can be tested without a push service and so a browser vendor's
/// endpoint is not something the application layer knows how to speak to.
/// </remarks>
public interface IPushSender
{
    Task<PushOutcome> SendAsync(
        PushSubscription subscription,
        string title,
        string body,
        string url,
        CancellationToken cancellationToken);
}

/// <summary>
/// Takes notifications off the outbox and delivers them.
/// </summary>
/// <remarks>
/// Rows are claimed with FOR UPDATE SKIP LOCKED, so a second relay can run beside this one without
/// the two fighting over the same notification or one waiting behind the other.
/// </remarks>
public sealed partial class NotificationRelay(
    IPitchwireDbContext db,
    IPushSender sender,
    TimeProvider clock,
    ILogger<NotificationRelay> logger)
{
    private const int MaxAttempts = 6;

    public async Task<int> DeliverAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var due = await db.NotificationOutbox
            .FromSql($"""
                SELECT * FROM notification_outbox
                WHERE sent_at IS NULL AND abandoned_reason IS NULL AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var deviceIds = due.Select(entry => entry.DeviceId).Distinct().ToList();

        var subscriptions = await db.PushSubscriptions
            .Where(subscription => deviceIds.Contains(subscription.DeviceId))
            .ToListAsync(cancellationToken);

        var delivered = 0;

        foreach (var entry in due)
        {
            var reachable = subscriptions.Where(s => s.DeviceId == entry.DeviceId).ToList();

            if (reachable.Count == 0)
            {
                // A device that asked to be told and then never subscribed, or whose subscription has
                // already been thrown away. There is nowhere to send this, and keeping it forever
                // would fill the table with work that can never succeed.
                Abandon(entry, "The device has no push subscription.", now);
                continue;
            }

            var anyDelivered = false;
            var anyRetryable = false;

            foreach (var subscription in reachable)
            {
                var outcome = await sender.SendAsync(
                    subscription,
                    entry.Title,
                    entry.Body,
                    $"/matches/{entry.MatchId}",
                    cancellationToken);

                switch (outcome)
                {
                    case PushOutcome.Delivered:
                        anyDelivered = true;
                        subscription.FailureCount = 0;
                        break;

                    case PushOutcome.Gone:
                        // Permanent. The browser is gone, the data was cleared, or the push service
                        // expired it, and none of those get better by asking again.
                        db.PushSubscriptions.Remove(subscription);
                        SubscriptionGone(logger, subscription.DeviceId);
                        break;

                    default:
                        anyRetryable = true;
                        subscription.FailureCount++;
                        break;
                }
            }

            if (anyDelivered)
            {
                entry.SentAt = now;
                delivered++;
            }
            else if (anyRetryable && entry.Attempts + 1 < MaxAttempts)
            {
                entry.Attempts++;

                // Exponential, so a push service having a bad minute is not hammered through it. A
                // goal that arrives late is still a goal; one that arrives an hour late is noise, and
                // the attempt ceiling is what stops that.
                entry.NextAttemptAt = now + TimeSpan.FromSeconds(Math.Pow(2, entry.Attempts));
            }
            else
            {
                entry.Attempts++;
                Abandon(entry, anyRetryable ? "Too many failed attempts." : "Every subscription was gone.", now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return delivered;
    }

    private static void Abandon(NotificationOutboxEntry entry, string reason, DateTimeOffset now)
    {
        entry.AbandonedReason = reason;
        entry.NextAttemptAt = now;
    }

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information,
        Message = "A push subscription for device {DeviceId} was refused permanently and has been removed.")]
    private static partial void SubscriptionGone(ILogger logger, Guid deviceId);
}
