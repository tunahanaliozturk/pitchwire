using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pitchwire.Api.Devices;
using Pitchwire.Contracts;
using Pitchwire.Infrastructure.Persistence;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// What a browser can do for itself without an account.
/// </summary>
/// <remarks>
/// The token in the cookie is the whole identity here, so the things worth pinning are that it is
/// issued, that nothing works without it, and that what the browser sends is checked before it can
/// quietly break notifications later.
/// </remarks>
public sealed class DeviceApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_first_visit_is_handed_an_identity_in_a_cookie_that_script_cannot_read()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        using var response = await client.PostAsync(new Uri("/devices", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.ShouldStartWith(DeviceEndpoints.CookieName);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
    }

    [Fact]
    public async Task Without_the_cookie_there_is_nobody_to_answer_about()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/devices/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_device_can_follow_a_team_and_read_it_back()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        await IdentifyAsync(client);

        var team = Catalogue.Teams[2];

        using var response = await client.PutAsJsonAsync(
            "/devices/me/favourites",
            new FavouritesRequest([team.Id]),
            Json,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var settings = await client.GetFromJsonAsync<DeviceSettings>("/devices/me", Json, TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.Favourites.ShouldBe([team.Id]);
    }

    [Fact]
    public async Task Following_a_team_nobody_has_is_ignored_rather_than_stored()
    {
        // A row pointing at a team that does not exist can never be shown and can never be followed
        // back to anything, and it would sit in the fan out query forever.
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        await IdentifyAsync(client);

        var settings = await (await client.PutAsJsonAsync(
            "/devices/me/favourites",
            new FavouritesRequest([Guid.NewGuid()]),
            Json,
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<DeviceSettings>(Json, TestContext.Current.CancellationToken);

        settings.ShouldNotBeNull();
        settings.Favourites.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_time_zone_this_service_cannot_resolve_is_refused_at_the_boundary()
    {
        // Accepting it would turn into quiet hours read in UTC months later, silencing notifications
        // at times the person who set them could never explain.
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        await IdentifyAsync(client);

        using var response = await client.PutAsJsonAsync(
            "/devices/me/preferences",
            new PreferencesRequest("Mars/Olympus_Mons", null, null, true, false, false, true),
            Json,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Quiet_hours_need_both_ends_or_neither()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        await IdentifyAsync(client);

        using var half = await client.PutAsJsonAsync(
            "/devices/me/preferences",
            new PreferencesRequest("Europe/Istanbul", new TimeOnly(22, 0), null, true, false, false, true),
            Json,
            TestContext.Current.CancellationToken);

        half.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var both = await client.PutAsJsonAsync(
            "/devices/me/preferences",
            new PreferencesRequest("Europe/Istanbul", new TimeOnly(22, 0), new TimeOnly(8, 0), true, false, false, true),
            Json,
            TestContext.Current.CancellationToken);

        both.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_device_cannot_take_over_an_existing_push_subscription()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var owner = app.CreateClient();
        using var stranger = app.CreateClient();
        await IdentifyAsync(owner);
        await IdentifyAsync(stranger);

        const string endpoint = "https://push.example.test/subscriptions/owned";
        using var registered = await owner.PostAsJsonAsync(
            "/devices/me/push-subscriptions",
            new SubscriptionRequest(endpoint, new SubscriptionKeys("owner-key", "owner-auth")),
            Json,
            TestContext.Current.CancellationToken);
        registered.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var renewed = await owner.PostAsJsonAsync(
            "/devices/me/push-subscriptions",
            new SubscriptionRequest(endpoint, new SubscriptionKeys("renewed-key", "renewed-auth")),
            Json,
            TestContext.Current.CancellationToken);
        renewed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var ownerSettings = await owner.GetFromJsonAsync<DeviceSettings>(
            "/devices/me", Json, TestContext.Current.CancellationToken);
        ownerSettings.ShouldNotBeNull();

        using var stolen = await stranger.PostAsJsonAsync(
            "/devices/me/push-subscriptions",
            new SubscriptionRequest(endpoint, new SubscriptionKeys("stranger-key", "stranger-auth")),
            Json,
            TestContext.Current.CancellationToken);
        stolen.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = postgres.CreateContext();
        var subscription = await db.PushSubscriptions.SingleAsync(TestContext.Current.CancellationToken);
        subscription.DeviceId.ShouldBe(ownerSettings.Id);
        subscription.P256dh.ShouldBe("renewed-key");
        subscription.Auth.ShouldBe("renewed-auth");
    }

    /// <summary>
    /// Takes the issued cookie and sends it back by hand.
    /// </summary>
    /// <remarks>
    /// The cookie is marked secure, so a client container will not return it over plain HTTP to the
    /// in memory server. A browser talking to the real service over HTTPS has no such problem, and
    /// weakening the cookie to make a test simpler would be the wrong trade.
    /// </remarks>
    private static async Task IdentifyAsync(HttpClient client)
    {
        using var issued = await client.PostAsync(new Uri("/devices", UriKind.Relative), content: null, TestContext.Current.CancellationToken);
        var token = issued.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        client.DefaultRequestHeaders.Add("Cookie", token);
    }

    private async Task SeedCatalogueAsync()
    {
        await using var db = postgres.CreateContext();
        await CatalogueSeeder.EnsureAsync(db, TestContext.Current.CancellationToken);
    }
}
