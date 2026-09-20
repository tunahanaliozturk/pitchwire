using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pitchwire.Application.Notifications;
using Pitchwire.Contracts;
using Pitchwire.Domain;
using Pitchwire.Infrastructure.Persistence;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// A push service that answers whatever the test needs it to.
/// </summary>
public sealed class FakePushSender(PushOutcome outcome = PushOutcome.Delivered) : IPushSender
{
    private readonly ConcurrentQueue<(Guid DeviceId, string Title, string Url)> _sent = new();

    public IReadOnlyCollection<(Guid DeviceId, string Title, string Url)> Sent => _sent;

    public PushOutcome Outcome { get; set; } = outcome;

    public Task<PushOutcome> SendAsync(
        Domain.PushSubscription subscription,
        string title,
        string body,
        string url,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        _sent.Enqueue((subscription.DeviceId, title, url));
        return Task.FromResult(Outcome);
    }
}

/// <summary>
/// Who gets told about a goal, and what happens to the message on its way out.
/// </summary>
/// <remarks>
/// Every failure here is quiet. A notification nobody receives looks exactly like a goal that was not
/// scored, and a notification sent to somebody who asked not to be disturbed is the kind of thing
/// that gets the whole product turned off in settings.
/// </remarks>
public sealed class NotificationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_goal_queues_a_notification_for_a_device_following_the_team()
    {
        var fixture = Catalogue.Fixtures[20];
        await SeedCatalogueAsync();
        await using var app = Factory(new FakePushSender());
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, fixture.Home.Id);

        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 12, forHome: true)]);

        var queued = await OutboxAsync(device);
        queued.Count.ShouldBe(1);
        queued[0].Title.ShouldBe($"Goal for {fixture.Home.Name}");
        queued[0].Body.ShouldContain("1-0");
        queued[0].MatchId.ShouldBe(fixture.Id);
    }

    [Fact]
    public async Task A_device_following_nobody_in_this_match_hears_nothing()
    {
        var fixture = Catalogue.Fixtures[21];
        var unrelated = Catalogue.Fixtures[22];
        await SeedCatalogueAsync();
        await using var app = Factory(new FakePushSender());
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, unrelated.Home.Id);

        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 12, forHome: true)]);

        (await OutboxAsync(device)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_event_type_turned_off_queues_nothing()
    {
        var fixture = Catalogue.Fixtures[23];
        await SeedCatalogueAsync();
        await using var app = Factory(new FakePushSender());
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, fixture.Home.Id, configure: d => d.NotifyOnGoal = false);

        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 12, forHome: true)]);

        (await OutboxAsync(device)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Quiet_hours_covering_now_queue_nothing()
    {
        // The window is set to swallow the whole day in the device's own zone, so the test does not
        // depend on what time it happens to run.
        var fixture = Catalogue.Fixtures[24];
        await SeedCatalogueAsync();
        await using var app = Factory(new FakePushSender());
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, fixture.Home.Id, configure: d =>
        {
            d.TimeZoneId = "UTC";
            d.QuietHoursStart = new TimeOnly(0, 0);
            d.QuietHoursEnd = new TimeOnly(23, 59);
        });

        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 12, forHome: true)]);

        (await OutboxAsync(device)).ShouldBeEmpty();
    }

    [Fact]
    public async Task One_event_never_queues_two_notifications_for_one_device()
    {
        // A provider redelivering a goal must not buzz a phone twice, and the constraint that stops
        // it is in the database rather than in a check somebody could forget to write.
        var fixture = Catalogue.Fixtures[25];
        await SeedCatalogueAsync();
        await using var app = Factory(new FakePushSender());
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, fixture.Home.Id);
        var goal = Payloads.Goal(ToMatch(fixture), 1, 12, forHome: true);

        await client.PostIngestAsync([goal]);
        await client.PostIngestAsync([goal]);

        (await OutboxAsync(device)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_relay_delivers_and_marks_the_entry_sent()
    {
        var fixture = Catalogue.Fixtures[26];
        var sender = new FakePushSender();
        await SeedCatalogueAsync();
        await using var app = Factory(sender);
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, fixture.Home.Id, subscribe: true);
        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 30, forHome: true)]);

        var delivered = await RelayAsync(app);

        delivered.ShouldBeGreaterThan(0);
        sender.Sent.ShouldContain(sent => sent.DeviceId == device && sent.Url == $"/matches/{fixture.Id}");
        (await OutboxAsync(device))[0].SentAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_subscription_the_push_service_has_finished_with_is_removed()
    {
        // Retrying a subscription that answered 410 forever is a slow leak: the table fills with work
        // that can never succeed and the relay spends its time on it.
        var fixture = Catalogue.Fixtures[27];
        var sender = new FakePushSender(PushOutcome.Gone);
        await SeedCatalogueAsync();
        await using var app = Factory(sender);
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, fixture.Home.Id, subscribe: true);
        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 30, forHome: true)]);

        await RelayAsync(app);

        await using var db = postgres.CreateContext();
        (await db.PushSubscriptions.CountAsync(s => s.DeviceId == device, TestContext.Current.CancellationToken)).ShouldBe(0);
        (await OutboxAsync(device))[0].AbandonedReason.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_temporary_failure_is_tried_again_later_rather_than_dropped()
    {
        var fixture = Catalogue.Fixtures[28];
        var sender = new FakePushSender(PushOutcome.Retry);
        await SeedCatalogueAsync();
        await using var app = Factory(sender);
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, fixture.Home.Id, subscribe: true);
        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 30, forHome: true)]);

        await RelayAsync(app);

        var entry = (await OutboxAsync(device))[0];
        entry.SentAt.ShouldBeNull();
        entry.AbandonedReason.ShouldBeNull();
        entry.Attempts.ShouldBe(1);

        // Backed off rather than retried immediately, so a push service having a bad minute is not
        // hammered through it.
        entry.NextAttemptAt.ShouldBeGreaterThan(entry.CreatedAt);
    }

    [Fact]
    public async Task A_device_that_never_subscribed_is_not_kept_waiting_forever()
    {
        var fixture = Catalogue.Fixtures[29];
        await SeedCatalogueAsync();
        await using var app = Factory(new FakePushSender());
        using var client = app.CreateClient();

        var device = await RegisterAsync(app, fixture.Home.Id, subscribe: false);
        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 30, forHome: true)]);

        await RelayAsync(app);

        (await OutboxAsync(device))[0].AbandonedReason.ShouldBe("The device has no push subscription.");
    }

    private PitchwireApiFactory Factory(IPushSender sender) =>
        new(postgres.ConnectionString, services =>
        {
            services.RemoveAll<IPushSender>();
            services.AddSingleton(sender);
        });

    private static async Task<Guid> RegisterAsync(
        PitchwireApiFactory app,
        Guid followedTeamId,
        Action<Device>? configure = null,
        bool subscribe = false)
    {
        using var scope = app.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<DeviceRegistry>();

        var (device, _) = await registry.IssueAsync(TestContext.Current.CancellationToken);
        configure?.Invoke(device);
        await registry.SaveAsync(TestContext.Current.CancellationToken);
        await registry.SetFavouritesAsync(device.Id, [followedTeamId], TestContext.Current.CancellationToken);

        if (subscribe)
        {
            await registry.SubscribeAsync(
                device.Id,
                $"https://push.invalid/{device.Id}",
                "test-p256dh",
                "test-auth",
                TestContext.Current.CancellationToken);
        }

        return device.Id;
    }

    private static async Task<int> RelayAsync(PitchwireApiFactory app)
    {
        using var scope = app.Services.CreateScope();
        var relay = scope.ServiceProvider.GetRequiredService<NotificationRelay>();

        return await relay.DeliverAsync(50, TestContext.Current.CancellationToken);
    }

    private async Task<List<NotificationOutboxEntry>> OutboxAsync(Guid deviceId)
    {
        await using var db = postgres.CreateContext();

        return await db.NotificationOutbox
            .AsNoTracking()
            .Where(entry => entry.DeviceId == deviceId)
            .OrderBy(entry => entry.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static Match ToMatch(CatalogueFixture fixture) => new()
    {
        Id = fixture.Id,
        SeasonId = Catalogue.SeasonId,
        Round = fixture.Round,
        KickoffUtc = fixture.KickoffUtc,
        HomeTeamId = fixture.Home.Id,
        AwayTeamId = fixture.Away.Id,
    };

    private async Task SeedCatalogueAsync()
    {
        await using var db = postgres.CreateContext();
        await CatalogueSeeder.EnsureAsync(db, TestContext.Current.CancellationToken);
    }
}
