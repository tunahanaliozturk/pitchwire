using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pitchwire.Api.Domain;
using Pitchwire.Api.Ingestion;
using Pitchwire.Api.Persistence;
using Pitchwire.Contracts;
using Pitchwire.Feed;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// A whole match, played by the simulated provider at its worst, checked against what the provider
/// itself believes happened.
/// </summary>
/// <remarks>
/// This is the test the milestone exists for. Duplication, reordering, dropped events and burst
/// pauses are all turned well above their defaults, so the run goes through every branch of the
/// ingestion pipeline rather than the happy path, and the score still has to match.
/// <para>
/// The comparison is against the provider's own tally, which is counted by different code from the
/// consumer's reducer. Comparing two runs of the same code would only prove it agrees with itself.
/// </para>
/// </remarks>
public sealed class FeedToApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task After_a_full_match_the_api_score_agrees_with_the_provider_ledger()
    {
        var ledger = new FeedLedger();
        await using var app = FactoryFor(ledger);
        using var client = app.CreateClient();
        await SeedCatalogueAsync();

        var fixture = Catalogue.Fixtures[0];
        var dispatcher = DispatcherFor(client, ledger);

        var run = await dispatcher.PlayAsync(fixture, TestContext.Current.CancellationToken);
        await SettleAsync(app, fixture.Id);

        // The run has to have gone wrong in every way the boundary claims to survive, or the rest of
        // this test is only checking the happy path under a misleading name.
        run.Dropped.ShouldBeGreaterThan(0);
        run.Duplicated.ShouldBeGreaterThan(0);
        run.Reordered.ShouldBeGreaterThan(0);

        var expected = ledger.ScoreFor(fixture.Id, fixture.Home.Id);
        var stored = await ReadMatchAsync(fixture.Id);

        stored.HomeScore.ShouldBe(expected.Home);
        stored.AwayScore.ShouldBe(expected.Away);
        stored.Status.ShouldBe(MatchStatus.Finished);
        stored.IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task Every_event_the_provider_generated_ends_up_stored_exactly_once()
    {
        var ledger = new FeedLedger();
        await using var app = FactoryFor(ledger);
        using var client = app.CreateClient();
        await SeedCatalogueAsync();

        var fixture = Catalogue.Fixtures[1];
        var dispatcher = DispatcherFor(client, ledger);

        var run = await dispatcher.PlayAsync(fixture, TestContext.Current.CancellationToken);
        await SettleAsync(app, fixture.Id);

        run.Dropped.ShouldBeGreaterThan(0);
        run.Duplicated.ShouldBeGreaterThan(0);

        var sent = ledger.From(fixture.Id, 1);

        await using var db = postgres.CreateContext();
        var stored = await db.MatchEvents
            .Where(e => e.MatchId == fixture.Id)
            .Select(e => e.Sequence)
            .ToListAsync(TestContext.Current.CancellationToken);

        stored.Count.ShouldBe(sent.Count);
        stored.Distinct().Count().ShouldBe(sent.Count);
    }

    private PitchwireApiFactory FactoryFor(FeedLedger ledger) =>
        new(postgres.ConnectionString, services =>
        {
            services.RemoveAll<IMatchFeed>();
            services.AddSingleton<IMatchFeed>(new LedgerMatchFeed(ledger));
        });

    private static FeedDispatcher DispatcherFor(HttpClient client, FeedLedger ledger)
    {
        var options = Options.Create(new FeedOptions
        {
            Secret = PitchwireApiFactory.IngestSecret,

            // Fast enough that a ninety minute match runs in well under a second, because the clock is
            // not what is under test here.
            ClockFactor = 100_000,
            Seed = 20_260_920,
            // Far above what any provider would manage on its worst day. A match carries about thirty
            // events, so anything gentler leaves a run with no drops in it, and the test would then
            // pass without the repair path ever running.
            DuplicateRate = 0.30,
            ReorderRate = 0.30,
            DropRate = 0.25,
            BurstPauseRate = 0.20,
        });

        return new FeedDispatcher(client, options, ledger, TimeProvider.System, NullLogger<FeedDispatcher>.Instance);
    }

    private async Task SeedCatalogueAsync()
    {
        await using var db = postgres.CreateContext();
        await CatalogueSeeder.EnsureAsync(db, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Waits for the recovery paths to finish what the provider left undone.
    /// </summary>
    /// <remarks>
    /// Gap repair runs in the background, and a dropped final whistle is only found by the sweep, so
    /// the sweep is run here rather than waited for. Both are the real components: nothing about the
    /// recovery is faked to make this pass.
    /// </remarks>
    private async Task SettleAsync(PitchwireApiFactory app, Guid matchId)
    {
        var deadline = DateTimeOffset.UtcNow + SettleTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using (var scope = app.Services.CreateScope())
            {
                var sweeper = scope.ServiceProvider.GetRequiredService<StaleMatchSweeper>();
                await sweeper.SweepAsync(TestContext.Current.CancellationToken);
            }

            var match = await ReadMatchAsync(matchId);

            if (match.Status == MatchStatus.Finished && !match.IsDegraded)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        }

        var stuck = await ReadMatchAsync(matchId);
        throw new InvalidOperationException(
            $"The match never settled. Status {stuck.Status}, degraded {stuck.IsDegraded}, " +
            $"last sequence {stuck.LastEventSequence}.");
    }

    private async Task<Match> ReadMatchAsync(Guid matchId)
    {
        await using var db = postgres.CreateContext();
        return await db.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId, TestContext.Current.CancellationToken);
    }
}
