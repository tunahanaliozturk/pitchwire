using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pitchwire.Application.Ingestion;
using Pitchwire.Application.Projections;
using Pitchwire.Contracts;
using Pitchwire.Domain;
using Pitchwire.Feed;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// The league table and the scorer list, against a match that really happened.
/// </summary>
/// <remarks>
/// A table is read far more often than it is written, and a wrong one is the kind of wrong that goes
/// unnoticed until somebody adds the points up by hand. These check it against the match it came
/// from, and check that recomputing it twice does not quietly double anything.
/// </remarks>
public sealed class ProjectionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_finished_match_lands_in_the_table_for_both_sides()
    {
        var fixture = Catalogue.Fixtures[3];
        var ledger = new FeedLedger();
        await using var app = FactoryFor(ledger);
        using var client = app.CreateClient();
        await SeedCatalogueAsync();

        await PlayAsync(app, client, ledger, fixture);

        var match = await ReadMatchAsync(fixture.Id);
        match.Status.ShouldBe(MatchStatus.Finished);

        await using var db = postgres.CreateContext();
        var home = await db.Standings.SingleAsync(s => s.TeamId == fixture.Home.Id, TestContext.Current.CancellationToken);
        var away = await db.Standings.SingleAsync(s => s.TeamId == fixture.Away.Id, TestContext.Current.CancellationToken);

        home.Played.ShouldBe(1);
        away.Played.ShouldBe(1);
        home.GoalsFor.ShouldBe(match.HomeScore);
        home.GoalsAgainst.ShouldBe(match.AwayScore);
        away.GoalsFor.ShouldBe(match.AwayScore);
        away.GoalsAgainst.ShouldBe(match.HomeScore);

        var expectedHomePoints = match.HomeScore > match.AwayScore ? 3 : match.HomeScore == match.AwayScore ? 1 : 0;
        var expectedAwayPoints = match.AwayScore > match.HomeScore ? 3 : match.HomeScore == match.AwayScore ? 1 : 0;

        home.Points.ShouldBe(expectedHomePoints);
        away.Points.ShouldBe(expectedAwayPoints);
    }

    [Fact]
    public async Task The_scorer_list_accounts_for_every_goal_except_the_ones_nobody_wanted()
    {
        var fixture = Catalogue.Fixtures[4];
        var ledger = new FeedLedger();
        await using var app = FactoryFor(ledger);
        using var client = app.CreateClient();
        await SeedCatalogueAsync();

        await PlayAsync(app, client, ledger, fixture);

        await using var db = postgres.CreateContext();

        // Counted across the season rather than this one match: the other tests in this class play
        // their own fixtures into the same database, and a per match figure would be right only when
        // this test happened to run first.
        var goalsInTheLog = await db.MatchEvents
            .Where(e => db.Matches.Any(m => m.Id == e.MatchId && m.SeasonId == Catalogue.SeasonId)
                && (e.Kind == Domain.MatchEventKind.Goal || e.Kind == Domain.MatchEventKind.PenaltyGoal))
            .CountAsync(TestContext.Current.CancellationToken);

        var goalsCredited = await db.PlayerSeasonStats
            .Where(s => s.SeasonId == Catalogue.SeasonId)
            .SumAsync(s => s.Goals, TestContext.Current.CancellationToken);

        // An own goal changes the score and credits nobody, so the two numbers agree only if the
        // projection made that distinction.
        goalsCredited.ShouldBe(goalsInTheLog);
    }

    [Fact]
    public async Task Projecting_the_same_season_twice_changes_nothing()
    {
        var fixture = Catalogue.Fixtures[5];
        var ledger = new FeedLedger();
        await using var app = FactoryFor(ledger);
        using var client = app.CreateClient();
        await SeedCatalogueAsync();

        await PlayAsync(app, client, ledger, fixture);

        var before = await ReadTableAsync();

        using (var scope = app.Services.CreateScope())
        {
            var projector = scope.ServiceProvider.GetRequiredService<SeasonProjector>();
            var db = scope.ServiceProvider.GetRequiredService<Pitchwire.Application.Persistence.IPitchwireDbContext>();

            await projector.ProjectAsync(Catalogue.SeasonId, TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var after = await ReadTableAsync();

        // A projection that adds to what is already there instead of replacing it would double every
        // number here, and would keep doubling on every later match.
        after.ShouldBe(before);
    }

    private PitchwireApiFactory FactoryFor(FeedLedger ledger) =>
        new(postgres.ConnectionString, services =>
        {
            services.RemoveAll<IMatchFeed>();
            services.AddSingleton<IMatchFeed>(new LedgerMatchFeed(ledger));
        });

    private async Task PlayAsync(
        PitchwireApiFactory app,
        HttpClient client,
        FeedLedger ledger,
        CatalogueFixture fixture)
    {
        var options = Options.Create(new FeedOptions
        {
            Secret = PitchwireApiFactory.IngestSecret,
            ClockFactor = 100_000,
            Seed = 4_242,
            DuplicateRate = 0.20,
            ReorderRate = 0.20,
            DropRate = 0.15,
            BurstPauseRate = 0.10,
        });

        var dispatcher = new FeedDispatcher(client, options, ledger, TimeProvider.System, NullLogger<FeedDispatcher>.Instance);
        await dispatcher.PlayAsync(fixture, TestContext.Current.CancellationToken);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            using (var scope = app.Services.CreateScope())
            {
                var sweeper = scope.ServiceProvider.GetRequiredService<StaleMatchSweeper>();
                await sweeper.SweepAsync(TestContext.Current.CancellationToken);
            }

            var match = await ReadMatchAsync(fixture.Id);

            if (match.Status == MatchStatus.Finished && !match.IsDegraded)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException("The match never settled, so the projection has nothing to be right about.");
    }

    private async Task SeedCatalogueAsync()
    {
        await using var db = postgres.CreateContext();
        await Pitchwire.Infrastructure.Persistence.CatalogueSeeder.EnsureAsync(db, TestContext.Current.CancellationToken);
    }

    private async Task<Match> ReadMatchAsync(Guid matchId)
    {
        await using var db = postgres.CreateContext();
        return await db.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId, TestContext.Current.CancellationToken);
    }

    private async Task<List<(Guid TeamId, int Played, int Points, int GoalsFor, int GoalsAgainst)>> ReadTableAsync()
    {
        await using var db = postgres.CreateContext();

        return await db.Standings
            .AsNoTracking()
            .Where(s => s.SeasonId == Catalogue.SeasonId)
            .OrderBy(s => s.TeamId)
            .Select(s => new ValueTuple<Guid, int, int, int, int>(s.TeamId, s.Played, s.Points, s.GoalsFor, s.GoalsAgainst))
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
