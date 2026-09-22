using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Reads;
using Pitchwire.Contracts;
using Pitchwire.Infrastructure.Persistence;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// Team sheets, statistics and the ratings derived from them.
/// </summary>
/// <remarks>
/// These are snapshots rather than events, so they have their own ordering rule: a sheet replaces
/// what was there and a statistics snapshot from an earlier minute is older news. Both are easy to
/// get wrong in a way nobody notices, because the wrong answer still looks like a number.
/// </remarks>
public sealed class SquadAndStatisticsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_countries_list_names_every_country_with_a_league()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var countries = await client.GetFromJsonAsync<List<CountrySummary>>("/countries", Json, TestContext.Current.CancellationToken);

        countries.ShouldNotBeNull();
        countries.Count.ShouldBe(Catalogue.Countries.Count);
        countries.ShouldAllBe(country => country.Leagues > 0);
        countries.Select(country => country.Name).ShouldContain("Türkiye");
    }

    [Fact]
    public async Task A_country_lists_its_leagues_with_the_top_flight_first()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var leagues = await client.GetFromJsonAsync<List<LeagueSummary>>(
            "/countries/turkiye/leagues",
            Json,
            TestContext.Current.CancellationToken);

        leagues.ShouldNotBeNull();
        leagues.Count.ShouldBe(2);
        leagues[0].Tier.ShouldBe(1);
        leagues[1].Tier.ShouldBe(2);
        leagues.ShouldAllBe(league => league.CurrentSeasonId != null);
    }

    [Fact]
    public async Task A_league_a_country_does_not_have_is_an_empty_list_rather_than_an_error()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var leagues = await client.GetFromJsonAsync<List<LeagueSummary>>(
            "/countries/atlantis/leagues",
            Json,
            TestContext.Current.CancellationToken);

        leagues.ShouldNotBeNull();
        leagues.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_team_sheet_arrives_with_the_match_and_is_shown_against_it()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var fixture = Catalogue.Fixtures[2];
        var sheet = SheetFor(fixture.Id, fixture.Home);

        await client.PostIngestAsync([], lineups: [sheet]);

        var detail = await client.GetFromJsonAsync<MatchDetail>($"/matches/{fixture.Id}", Json, TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        var side = detail.Lineups.ShouldHaveSingleItem();
        side.TeamId.ShouldBe(fixture.Home.Id);
        side.Formation.ShouldBe("4-3-3");
        side.Players.Count(player => player.Starter).ShouldBe(11);
        side.Players.ShouldAllBe(player => player.Player != "Unknown");
    }

    [Fact]
    public async Task A_corrected_team_sheet_replaces_the_one_before_it()
    {
        // A player dropped from a corrected sheet has to disappear. Merging would leave twelve players
        // starting, which is the kind of number a reader counts.
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var fixture = Catalogue.Fixtures[3];
        var first = SheetFor(fixture.Id, fixture.Home);
        var corrected = first with
        {
            Formation = "3-5-2",
            Players = [.. first.Players.Take(12)],
        };

        await client.PostIngestAsync([], lineups: [first]);
        await client.PostIngestAsync([], lineups: [corrected]);

        var detail = await client.GetFromJsonAsync<MatchDetail>($"/matches/{fixture.Id}", Json, TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        var side = detail.Lineups.ShouldHaveSingleItem();
        side.Formation.ShouldBe("3-5-2");
        side.Players.Count.ShouldBe(12);
    }

    [Fact]
    public async Task Statistics_from_an_earlier_minute_are_ignored()
    {
        // Cumulative numbers only ever grow, so a late arriving old snapshot would walk the shot count
        // backwards, which is the statistics version of a score going down.
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var fixture = Catalogue.Fixtures[4];

        await client.PostIngestAsync([], statistics:
        [
            new StatisticsPayload(fixture.Id, fixture.Home.Id, 75, 55, 14, 6, 7, 11, 2),
        ]);

        await client.PostIngestAsync([], statistics:
        [
            new StatisticsPayload(fixture.Id, fixture.Home.Id, 30, 51, 5, 2, 3, 4, 1),
        ]);

        var detail = await client.GetFromJsonAsync<MatchDetail>($"/matches/{fixture.Id}", Json, TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        var stats = detail.Statistics.ShouldHaveSingleItem();
        stats.AsOfMinute.ShouldBe(75);
        stats.Shots.ShouldBe(14);
    }

    [Fact]
    public async Task A_newer_snapshot_replaces_the_one_before_it()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var fixture = Catalogue.Fixtures[5];

        await client.PostIngestAsync([], statistics:
        [
            new StatisticsPayload(fixture.Id, fixture.Away.Id, 30, 48, 5, 2, 3, 6, 0),
        ]);

        await client.PostIngestAsync([], statistics:
        [
            new StatisticsPayload(fixture.Id, fixture.Away.Id, 90, 46, 12, 5, 8, 13, 3),
        ]);

        var detail = await client.GetFromJsonAsync<MatchDetail>($"/matches/{fixture.Id}", Json, TestContext.Current.CancellationToken);

        detail!.Statistics.ShouldHaveSingleItem().Shots.ShouldBe(12);
    }

    [Fact]
    public async Task A_player_who_scored_is_rated_above_one_who_watched()
    {
        // The rating is derived from the log, so this is really a check that the derivation reached the
        // page: the same events, the same minutes, and a number that moved for the right player.
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var fixture = Catalogue.Fixtures[6];
        var scorer = fixture.Home.Players.First(player => player.Position == LineupPosition.Forward);
        var quiet = fixture.Home.Players.First(player => player.Position == LineupPosition.Midfielder);

        await client.PostIngestAsync(
        [
            Payloads.Event(ToMatch(fixture), 1, 0, MatchEventKind.PeriodStart, forHome: true),
            Payloads.Goal(ToMatch(fixture), 2, 20, forHome: true) with { PlayerId = scorer.Id },
            Payloads.Event(ToMatch(fixture), 3, 92, MatchEventKind.PeriodEnd, forHome: true),
        ],
            lineups: [SheetFor(fixture.Id, fixture.Home)]);

        var detail = await client.GetFromJsonAsync<MatchDetail>($"/matches/{fixture.Id}", Json, TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        var players = detail.Lineups.ShouldHaveSingleItem().Players;

        var scored = players.Single(player => player.PlayerId == scorer.Id);
        var watched = players.Single(player => player.PlayerId == quiet.Id);

        scored.Goals.ShouldBe(1);
        scored.MinutesPlayed.ShouldBe(92);
        scored.Rating.ShouldNotBeNull();
        watched.Rating.ShouldNotBeNull();
        scored.Rating!.Value.ShouldBeGreaterThan(watched.Rating!.Value);
    }

    [Fact]
    public async Task A_substitute_who_never_came_on_is_not_given_a_rating()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var fixture = Catalogue.Fixtures[7];

        await client.PostIngestAsync(
        [
            Payloads.Event(ToMatch(fixture), 1, 0, MatchEventKind.PeriodStart, forHome: true),
            Payloads.Event(ToMatch(fixture), 2, 90, MatchEventKind.PeriodEnd, forHome: true),
        ],
            lineups: [SheetFor(fixture.Id, fixture.Home)]);

        var detail = await client.GetFromJsonAsync<MatchDetail>($"/matches/{fixture.Id}", Json, TestContext.Current.CancellationToken);

        var bench = detail!.Lineups.ShouldHaveSingleItem().Players.Where(player => !player.Starter).ToList();

        bench.ShouldNotBeEmpty();
        bench.ShouldAllBe(player => player.MinutesPlayed == 0 && player.Rating == null);
    }

    private static LineupPayload SheetFor(Guid matchId, CatalogueTeam team)
    {
        var starters = new List<CataloguePlayer>();
        starters.AddRange(team.Players.Where(p => p.Position == LineupPosition.Goalkeeper).Take(1));
        starters.AddRange(team.Players.Where(p => p.Position == LineupPosition.Defender).Take(4));
        starters.AddRange(team.Players.Where(p => p.Position == LineupPosition.Midfielder).Take(3));
        starters.AddRange(team.Players.Where(p => p.Position == LineupPosition.Forward).Take(3));

        return new LineupPayload(
            matchId,
            team.Id,
            "4-3-3",
            [
                .. team.Players.Select(player => new LineupPlayerPayload(
                    player.Id,
                    player.ShirtNumber,
                    player.Position,
                    starters.Contains(player))),
            ]);
    }

    private static Domain.Match ToMatch(CatalogueFixture fixture) => new()
    {
        Id = fixture.Id,
        SeasonId = fixture.SeasonId,
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
