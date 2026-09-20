using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pitchwire.Application.Paging;
using Pitchwire.Application.Reads;
using Pitchwire.Contracts;
using Pitchwire.Infrastructure.Persistence;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// The read side, and the paging a client has to trust.
/// </summary>
/// <remarks>
/// A reader pages through a list once and expects to see each row once. Both ways of getting that
/// wrong are silent: an offset that skips a row when the set shifts, and a token that is quietly
/// treated as the beginning when it cannot be read. These are the tests that would catch either.
/// </remarks>
public sealed class ReadApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Paging_through_the_fixture_list_visits_every_match_exactly_once()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var seen = new List<Guid>();
        var next = $"/seasons/{Catalogue.SeasonId}/fixtures?$top=7";

        // A bound, so a paging bug that loops forever fails as a test rather than as a hung suite.
        for (var request = 0; request < 50 && next is not null; request++)
        {
            var page = await client.GetFromJsonAsync<Page<MatchSummary>>(next, Json, TestContext.Current.CancellationToken);

            page.ShouldNotBeNull();
            page.Value.Count.ShouldBeLessThanOrEqualTo(7);
            seen.AddRange(page.Value.Select(match => match.Id));

            next = page.NextLink;
        }

        next.ShouldBeNull("the list should have ended within the bound");
        seen.Count.ShouldBe(Catalogue.Fixtures.Count);
        seen.Distinct().Count().ShouldBe(seen.Count);
        seen.ShouldBe(Catalogue.Fixtures.OrderBy(f => f.KickoffUtc).ThenBy(f => f.Id).Select(f => f.Id), ignoreOrder: false);
    }

    [Fact]
    public async Task The_last_page_carries_no_link_at_all()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var page = await client.GetFromJsonAsync<Page<MatchSummary>>(
            $"/seasons/{Catalogue.SeasonId}/fixtures?$top=100",
            Json,
            TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Value.Count.ShouldBe(Catalogue.Fixtures.Count);
        page.NextLink.ShouldBeNull();
    }

    [Fact]
    public async Task A_page_size_beyond_the_cap_is_brought_back_to_it()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var page = await client.GetFromJsonAsync<Page<MatchSummary>>(
            $"/seasons/{Catalogue.SeasonId}/fixtures?$top=5000",
            Json,
            TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Value.Count.ShouldBeLessThanOrEqualTo(PageSize.Max);
    }

    [Fact]
    public async Task A_token_that_cannot_be_read_is_refused_rather_than_ignored()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/seasons/{Catalogue.SeasonId}/fixtures?$skiptoken=nonsense", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_token_belonging_to_another_list_is_refused()
    {
        // Handing a results cursor to the fixture list would otherwise put the reader at a position
        // nobody asked for, and the response would look perfectly normal.
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var foreign = SkipToken.Encode(MatchReads.ResultsToken, new MatchCursor(DateTimeOffset.UtcNow, Guid.NewGuid()));

        using var response = await client.GetAsync(
            new Uri($"/seasons/{Catalogue.SeasonId}/fixtures?$skiptoken={foreign}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_round_filter_narrows_the_fixture_list()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var page = await client.GetFromJsonAsync<Page<MatchSummary>>(
            $"/seasons/{Catalogue.SeasonId}/fixtures?round=3&$top=100",
            Json,
            TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Value.ShouldAllBe(match => match.Round == 3);
        page.Value.Count.ShouldBe(Catalogue.Fixtures.Count(f => f.Round == 3));
    }

    [Fact]
    public async Task A_match_nobody_has_is_a_not_found_rather_than_an_empty_one()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/matches/{Guid.NewGuid()}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_match_detail_carries_its_timeline_in_order()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var fixture = Catalogue.Fixtures[0];
        await client.PostIngestAsync(
        [
            Payloads.Event(ToMatch(fixture), 1, 0, Contracts.MatchEventKind.PeriodStart, forHome: true),
            Payloads.Goal(ToMatch(fixture), 2, 23, forHome: true),
            Payloads.Goal(ToMatch(fixture), 3, 67, forHome: false),
        ]);

        var detail = await client.GetFromJsonAsync<MatchDetail>(
            $"/matches/{fixture.Id}",
            Json,
            TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        detail.Match.HomeScore.ShouldBe(1);
        detail.Match.AwayScore.ShouldBe(1);
        detail.Match.Home.Name.ShouldBe(fixture.Home.Name);
        detail.Timeline.Select(e => e.Sequence).ShouldBe([1, 2, 3]);
        detail.Timeline[1].Kind.ShouldBe("Goal");
    }

    [Fact]
    public async Task The_table_puts_the_leader_first_and_numbers_the_rows()
    {
        await SeedCatalogueAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var rows = await client.GetFromJsonAsync<List<TableRow>>(
            $"/seasons/{Catalogue.SeasonId}/table",
            Json,
            TestContext.Current.CancellationToken);

        rows.ShouldNotBeNull();
        rows.Count.ShouldBe(Catalogue.Teams.Count);
        rows.Select(row => row.Position).ShouldBe(Enumerable.Range(1, rows.Count));
        rows.ShouldBeInOrder(SortDirection.Descending, Comparer<TableRow>.Create((left, right) =>
            left.Points != right.Points
                ? left.Points.CompareTo(right.Points)
                : left.GoalDifference.CompareTo(right.GoalDifference)));
    }

    [Fact]
    public async Task The_api_describes_itself()
    {
        // A read API nobody can read the shape of is a read API nobody can use. The document is part
        // of the contract, so it is checked like the rest of it.
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var document = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        document.ShouldContain("/matches/live");
        document.ShouldContain("/seasons/{seasonId}/table");
        document.ShouldContain("/ingest/events");
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
