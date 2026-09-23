using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Paging;
using Pitchwire.Application.Reads;
using Pitchwire.Contracts;
using Pitchwire.Domain;
using Pitchwire.Infrastructure.Persistence;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>Season choices and filtered continuation links, against PostgreSQL rather than a fake query provider.</summary>
public sealed class MatchFilterTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Season_detail_lists_actual_participants_and_rounds()
    {
        await SeedAsync();
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        var detail = await client.GetFromJsonAsync<SeasonDetail>(
            $"/seasons/{Catalogue.SeasonId}", Json, TestContext.Current.CancellationToken);

        detail.ShouldNotBeNull();
        detail.Teams.Select(team => team.Id).ShouldBe(
            Catalogue.Teams.Select(team => team.Id), ignoreOrder: true);
        detail.Rounds.ShouldBe(
            Catalogue.Fixtures.Select(fixture => fixture.Round).Distinct().OrderBy(round => round));

        using var missing = await client.GetAsync(
            new Uri($"/seasons/{Guid.NewGuid()}", UriKind.Relative), TestContext.Current.CancellationToken);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Team_and_round_filters_survive_every_continuation_page()
    {
        await SeedAsync();
        var teamId = Catalogue.Teams[0].Id;
        var teamsMatches = Catalogue.Fixtures
            .Where(fixture => fixture.Home.Id == teamId || fixture.Away.Id == teamId)
            .OrderBy(fixture => fixture.KickoffUtc)
            .ToList();
        var finished = teamsMatches.Take(3).Select(fixture => fixture.Id).ToList();
        var unrelated = Catalogue.Fixtures.First(fixture => fixture.Home.Id != teamId && fixture.Away.Id != teamId);
        finished.Add(unrelated.Id);

        await using (var db = postgres.CreateContext())
        {
            await db.Matches
                .Where(match => finished.Contains(match.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(match => match.Status, MatchStatus.Finished),
                    TestContext.Current.CancellationToken);
        }

        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        // Prime the unfiltered cache with the same page sizes. A missing team in the cache key would
        // otherwise make the next requests look valid while returning somebody else's list.
        await client.GetFromJsonAsync<Page<MatchSummary>>(
            $"/seasons/{Catalogue.SeasonId}/fixtures?$top=2", Json, TestContext.Current.CancellationToken);
        await client.GetFromJsonAsync<Page<MatchSummary>>(
            $"/seasons/{Catalogue.SeasonId}/results?$top=1", Json, TestContext.Current.CancellationToken);

        var fixtureIds = await CollectAsync(
            client, $"/seasons/{Catalogue.SeasonId}/fixtures?teamId={teamId}&$top=2", teamId);
        fixtureIds.ShouldBe(
            teamsMatches.Skip(3).OrderBy(fixture => fixture.KickoffUtc).Select(fixture => fixture.Id));

        var resultIds = await CollectAsync(
            client, $"/seasons/{Catalogue.SeasonId}/results?teamId={teamId}&$top=1", teamId);
        resultIds.ShouldBe(
            teamsMatches.Take(3).OrderByDescending(fixture => fixture.KickoffUtc).Select(fixture => fixture.Id));

        var wantedRound = teamsMatches[3].Round;
        var roundPage = await client.GetFromJsonAsync<Page<MatchSummary>>(
            $"/seasons/{Catalogue.SeasonId}/fixtures?teamId={teamId}&round={wantedRound}&$top=2",
            Json, TestContext.Current.CancellationToken);
        roundPage.ShouldNotBeNull();
        roundPage.Value.ShouldHaveSingleItem().Id.ShouldBe(teamsMatches[3].Id);

        var finishedRound = teamsMatches[0].Round;
        var resultRound = await client.GetFromJsonAsync<Page<MatchSummary>>(
            $"/seasons/{Catalogue.SeasonId}/results?teamId={teamId}&round={finishedRound}&$top=2",
            Json, TestContext.Current.CancellationToken);
        resultRound.ShouldNotBeNull();
        resultRound.Value.ShouldHaveSingleItem().Id.ShouldBe(teamsMatches[0].Id);

        var roundIds = new List<Guid>();
        string? nextRound = $"/seasons/{Catalogue.SeasonId}/fixtures?round={wantedRound}&$top=2";

        for (var request = 0; request < 10 && nextRound is not null; request++)
        {
            var page = await client.GetFromJsonAsync<Page<MatchSummary>>(
                nextRound, Json, TestContext.Current.CancellationToken);
            page.ShouldNotBeNull();
            page.Value.ShouldAllBe(match => match.Round == wantedRound);
            roundIds.AddRange(page.Value.Select(match => match.Id));
            nextRound = page.NextLink;
            if (nextRound is not null)
            {
                nextRound.ShouldContain($"round={wantedRound}");
            }
        }

        nextRound.ShouldBeNull("round paging should end within the bound");
        roundIds.ShouldBe(
            Catalogue.Fixtures
                .Where(fixture => fixture.Round == wantedRound && !finished.Contains(fixture.Id))
                .OrderBy(fixture => fixture.KickoffUtc)
                .ThenBy(fixture => fixture.Id)
                .Select(fixture => fixture.Id));
    }

    private static async Task<List<Guid>> CollectAsync(HttpClient client, string path, Guid teamId)
    {
        var seen = new List<Guid>();
        string? next = path;

        for (var request = 0; request < 20 && next is not null; request++)
        {
            var page = await client.GetFromJsonAsync<Page<MatchSummary>>(
                next, Json, TestContext.Current.CancellationToken);
            page.ShouldNotBeNull();
            page.Value.ShouldAllBe(match => match.Home.Id == teamId || match.Away.Id == teamId);
            seen.AddRange(page.Value.Select(match => match.Id));
            next = page.NextLink;
            if (next is not null)
            {
                next.ShouldContain($"teamId={teamId}");
            }
        }

        next.ShouldBeNull("filtered paging should end within the bound");
        seen.Distinct().Count().ShouldBe(seen.Count);
        return seen;
    }

    private async Task SeedAsync()
    {
        await using var db = postgres.CreateContext();
        await CatalogueSeeder.EnsureAsync(db, TestContext.Current.CancellationToken);
    }
}
