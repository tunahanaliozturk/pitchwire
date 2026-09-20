using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Pitchwire.Application.Live;
using Pitchwire.Application.Reads;
using Pitchwire.Contracts;
using Pitchwire.Infrastructure.Persistence;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// What a watching browser is told, and what it is not.
/// </summary>
/// <remarks>
/// The promise of a live score service is that a goal appears without anybody asking. These connect a
/// real client to the hub and check that the goal arrives, that it arrives at the people who asked
/// for it, and that it does not arrive at the people who did not.
/// </remarks>
public sealed class LiveUpdateTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_goal_reaches_a_client_watching_that_match()
    {
        var fixture = Catalogue.Fixtures[10];
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        await using var connection = Connect(app);
        var updates = Collect(connection);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync("WatchMatch", fixture.Id, TestContext.Current.CancellationToken);

        await client.PostIngestAsync(
        [
            Payloads.Event(ToMatch(fixture), 1, 0, MatchEventKind.PeriodStart, forHome: true),
            Payloads.Goal(ToMatch(fixture), 2, 18, forHome: true),
        ]);

        var goal = await WaitForAsync(updates, update => update.Event?.Kind == "Goal");

        goal.MatchId.ShouldBe(fixture.Id);
        goal.HomeScore.ShouldBe(1);
        goal.AwayScore.ShouldBe(0);
        goal.Minute.ShouldBe(18);
        goal.Status.ShouldBe("Live");
        goal.Event.ShouldNotBeNull();
        goal.Event.Sequence.ShouldBe(2);
        goal.Event.TeamId.ShouldBe(fixture.Home.Id);
    }

    [Fact]
    public async Task A_goal_reaches_a_client_following_one_of_the_teams()
    {
        // The favourite case. A client that never opened this match still hears about it, which is
        // the whole reason a team group exists.
        var fixture = Catalogue.Fixtures[11];
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        await using var connection = Connect(app);
        var updates = Collect(connection);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync("WatchTeam", fixture.Away.Id, TestContext.Current.CancellationToken);

        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 33, forHome: false)]);

        var goal = await WaitForAsync(updates, update => update.MatchId == fixture.Id);

        goal.AwayTeamId.ShouldBe(fixture.Away.Id);
        goal.AwayScore.ShouldBe(1);
    }

    [Fact]
    public async Task A_client_watching_another_match_hears_nothing()
    {
        // Without this, every group would be the live group wearing a different name, and a phone on
        // one match detail would be paying for every other match in the country.
        var watched = Catalogue.Fixtures[12];
        var other = Catalogue.Fixtures[13];
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        await using var connection = Connect(app);
        var updates = Collect(connection);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync("WatchMatch", watched.Id, TestContext.Current.CancellationToken);

        await client.PostIngestAsync([Payloads.Goal(ToMatch(other), 1, 12, forHome: true)]);
        await client.PostIngestAsync([Payloads.Goal(ToMatch(watched), 1, 20, forHome: true)]);

        // Waiting for the one that should arrive is what makes the absence of the other meaningful:
        // by the time this returns, the unwanted update has had its chance.
        await WaitForAsync(updates, update => update.MatchId == watched.Id);

        updates.ShouldAllBe(update => update.MatchId == watched.Id);
    }

    [Fact]
    public async Task The_live_group_hears_about_every_match()
    {
        var fixture = Catalogue.Fixtures[14];
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        await using var connection = Connect(app);
        var updates = Collect(connection);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync("WatchLive", TestContext.Current.CancellationToken);

        await client.PostIngestAsync([Payloads.Goal(ToMatch(fixture), 1, 5, forHome: true)]);

        (await WaitForAsync(updates, update => update.MatchId == fixture.Id)).HomeScore.ShouldBe(1);
    }

    [Fact]
    public async Task A_reconnecting_client_can_ask_for_what_it_missed()
    {
        // A socket that comes back has no idea what happened while it was gone. Without this the
        // timeline would either show a hole or have to be fetched from the first minute again.
        var fixture = Catalogue.Fixtures[15];
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        await client.PostIngestAsync(
        [
            Payloads.Event(ToMatch(fixture), 1, 0, MatchEventKind.PeriodStart, forHome: true),
            Payloads.Goal(ToMatch(fixture), 2, 10, forHome: true),
            Payloads.Goal(ToMatch(fixture), 3, 40, forHome: false),
            Payloads.Goal(ToMatch(fixture), 4, 70, forHome: true),
        ]);

        var missed = await client.GetFromJsonAsync<List<MatchEventView>>(
            $"/matches/{fixture.Id}/events?from=3",
            Json,
            TestContext.Current.CancellationToken);

        missed.ShouldNotBeNull();
        missed.Select(e => e.Sequence).ShouldBe([3, 4]);
        missed[0].Kind.ShouldBe("Goal");
    }

    private static HubConnection Connect(PitchwireApiFactory app) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(app.Server.BaseAddress, "hub/matches"), options =>
            {
                options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();

                // Long polling rather than a socket, because the in memory test server does not carry
                // one. The hub code under test is the same either way.
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    private static List<MatchUpdate> Collect(HubConnection connection)
    {
        var received = new List<MatchUpdate>();

        connection.On<MatchUpdate>("matchUpdated", update =>
        {
            lock (received)
            {
                received.Add(update);
            }
        });

        return received;
    }

    private static async Task<MatchUpdate> WaitForAsync(List<MatchUpdate> updates, Func<MatchUpdate, bool> wanted)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (updates)
            {
                var found = updates.FirstOrDefault(wanted);

                if (found is not null)
                {
                    return found;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException($"No matching update arrived within {Patience.TotalSeconds} seconds.");
    }

    private static Domain.Match ToMatch(CatalogueFixture fixture) => new()
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
