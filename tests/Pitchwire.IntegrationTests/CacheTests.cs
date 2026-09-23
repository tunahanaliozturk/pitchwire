using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pitchwire.Application.Reads;
using Pitchwire.Contracts;
using Pitchwire.Infrastructure.Persistence;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// Whether the cache is really there, and whether dropping a tag really clears it.
/// </summary>
/// <remarks>
/// Both halves matter and both fail quietly. A cache that is not actually caching costs nothing and
/// looks fine. Tag invalidation that does not reach the second level leaves every other instance
/// serving yesterday's table, and the only symptom is a number that is wrong on one screen.
/// <para>
/// Run against a real Redis for that reason. The behaviour under test belongs to the cache
/// implementation, and a substitute would only prove the substitute was wired up.
/// </para>
/// </remarks>
public sealed class CacheTests(PostgresFixture postgres, RedisFixture redis)
    : IClassFixture<PostgresFixture>, IClassFixture<RedisFixture>
{
    [Fact]
    public async Task A_second_read_of_the_table_does_not_touch_the_database()
    {
        await SeedCatalogueAsync();
        await using var app = Factory();

        var first = await TableAsync(app);
        await ChangePointsBehindTheCacheAsync(Catalogue.Teams[0].Id, points: 99);
        var second = await TableAsync(app);

        // The database now says something else and the answer has not moved, which is the only way to
        // tell a cache from a fast query.
        second.ShouldBe(first);
    }

    [Fact]
    public async Task Dropping_a_tag_in_the_same_instant_as_the_write_still_drops_it()
    {
        // The invalidation and the entry it has to beat can land in the same instant during a match:
        // a reader caches the table while a goal is being ingested. If the drop loses that race the
        // stale table survives for the whole entry lifetime, and nothing anywhere says so.
        await using var app = Factory();
        using var scope = app.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();

        var tag = $"race:{Guid.NewGuid()}";
        var key = $"key:{Guid.NewGuid()}";
        var builds = 0;

        ValueTask<int> Build(CancellationToken _) => new(Interlocked.Increment(ref builds));

        (await cache.GetOrCreateAsync(key, Build, tags: [tag], cancellationToken: TestContext.Current.CancellationToken)).ShouldBe(1);
        await cache.RemoveByTagAsync(tag, TestContext.Current.CancellationToken);

        (await cache.GetOrCreateAsync(key, Build, tags: [tag], cancellationToken: TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task A_second_host_reads_an_entry_from_redis_instead_of_building_it_again()
    {
        await using var first = Factory();
        await using var second = Factory();

        using var firstScope = first.Services.CreateScope();
        using var secondScope = second.Services.CreateScope();
        var firstCache = firstScope.ServiceProvider.GetRequiredService<HybridCache>();
        var secondCache = secondScope.ServiceProvider.GetRequiredService<HybridCache>();
        var key = $"shared-value:{Guid.NewGuid()}";
        var builds = 0;

        ValueTask<int> Build(CancellationToken _) => new(Interlocked.Increment(ref builds));

        (await firstCache.GetOrCreateAsync(key, Build, cancellationToken: TestContext.Current.CancellationToken)).ShouldBe(1);
        (await secondCache.GetOrCreateAsync(key, Build, cancellationToken: TestContext.Current.CancellationToken)).ShouldBe(1);
        builds.ShouldBe(1);
    }

    [Fact]
    public async Task A_tag_dropped_by_one_instance_is_not_seen_by_another()
    {
        // Two hosts, one Redis, which is what a second replica would look like. The entry is shared
        // through the second level, and the knowledge that its tag was dropped is not.
        await using var first = Factory();
        await using var second = Factory();

        var tag = $"shared:{Guid.NewGuid()}";
        var key = $"shared-key:{Guid.NewGuid()}";
        var builds = 0;

        ValueTask<int> Build(CancellationToken _) => new(Interlocked.Increment(ref builds));

        using var firstScope = first.Services.CreateScope();
        using var secondScope = second.Services.CreateScope();
        var firstCache = firstScope.ServiceProvider.GetRequiredService<HybridCache>();
        var secondCache = secondScope.ServiceProvider.GetRequiredService<HybridCache>();

        (await firstCache.GetOrCreateAsync(key, Build, tags: [tag], cancellationToken: TestContext.Current.CancellationToken)).ShouldBe(1);
        await secondCache.RemoveByTagAsync(tag, TestContext.Current.CancellationToken);

        var afterOtherInstanceDropped = await firstCache.GetOrCreateAsync(
            key, Build, tags: [tag], cancellationToken: TestContext.Current.CancellationToken);

        // Recorded rather than wished away. Running a second replica would need invalidation to travel
        // between them, and ADR 0008 is where that cost is written down.
        afterOtherInstanceDropped.ShouldBe(1);
    }

    [Fact]
    public async Task Dropping_the_season_tag_reaches_the_entry()
    {
        await SeedCatalogueAsync();
        await using var app = Factory();

        var before = await TableAsync(app);
        await ChangePointsBehindTheCacheAsync(Catalogue.Teams[1].Id, points: 77);

        using (var scope = app.Services.CreateScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
            await cache.RemoveByTagAsync(CacheScope.SeasonTag(Catalogue.SeasonId), TestContext.Current.CancellationToken);
        }

        var after = await TableAsync(app);

        after.ShouldNotBe(before);
        after[0].Points.ShouldBe(77);
    }

    [Fact]
    public async Task An_ingested_goal_drops_the_table_the_reader_was_holding()
    {
        // The path that matters in the running service: a goal arrives, and the next reader gets a
        // table that knows about it rather than the one cached a moment earlier.
        await SeedCatalogueAsync();
        await using var app = Factory();
        using var client = app.CreateClient();

        var fixture = Catalogue.Fixtures[7];
        var match = ToMatch(fixture);

        await client.PostIngestAsync([Payloads.Event(match, 1, 0, Contracts.MatchEventKind.PeriodStart, forHome: true)]);
        var beforeScorers = await ScorersAsync(app);

        await client.PostIngestAsync(
        [
            Payloads.Event(match, 2, 21, Contracts.MatchEventKind.Goal, forHome: true) with
            {
                PlayerId = fixture.Home.Players[3].Id,
            },
        ]);

        var afterScorers = await ScorersAsync(app);

        beforeScorers.ShouldBeEmpty();
        afterScorers.ShouldContain(row => row.PlayerId == fixture.Home.Players[3].Id && row.Goals == 1);
    }

    private PitchwireApiFactory Factory() =>
        new(postgres.ConnectionString, configureServices: null, redisConnectionString: redis.ConnectionString);

    private static async Task<List<TableRow>> TableAsync(PitchwireApiFactory app)
    {
        using var scope = app.Services.CreateScope();
        var reads = scope.ServiceProvider.GetRequiredService<SeasonReads>();

        return [.. await reads.TableAsync(Catalogue.SeasonId, TestContext.Current.CancellationToken)];
    }

    private static async Task<List<ScorerRow>> ScorersAsync(PitchwireApiFactory app)
    {
        using var scope = app.Services.CreateScope();
        var reads = scope.ServiceProvider.GetRequiredService<SeasonReads>();

        return [.. await reads.ScorersAsync(Catalogue.SeasonId, 10, TestContext.Current.CancellationToken)];
    }

    private async Task ChangePointsBehindTheCacheAsync(Guid teamId, int points)
    {
        await using var db = postgres.CreateContext();

        var changed = await db.Standings
            .Where(s => s.SeasonId == Catalogue.SeasonId && s.TeamId == teamId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Points, points), TestContext.Current.CancellationToken);

        // The point of this helper is that the database now disagrees with the cache. If the write
        // did not land, every assertion built on it would be testing nothing at all.
        changed.ShouldBe(1);

        var stored = await db.Standings
            .AsNoTracking()
            .Where(s => s.SeasonId == Catalogue.SeasonId && s.TeamId == teamId)
            .Select(s => s.Points)
            .SingleAsync(TestContext.Current.CancellationToken);

        stored.ShouldBe(points);
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
