using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pitchwire.Api.Domain;
using Pitchwire.Api.Ingestion;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// What happens when the provider delivers out of order, or not at all.
/// </summary>
/// <remarks>
/// A push feed is wrong until someone asks. A missed event stays missed, and a late one arrives after
/// the score has already moved on, so both have to be handled on the way in rather than noticed later
/// by a reader who has no way to tell.
/// </remarks>
public sealed class OrderingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Events_delivered_out_of_order_produce_the_score_they_would_have_in_order()
    {
        var match = await SeedMatchAsync();
        var missing = Payloads.Goal(match, sequence: 2, minute: 31, forHome: false);
        await using var app = FactoryWith(new StubMatchFeed(missing));
        using var client = app.CreateClient();

        await client.PostIngestAsync([Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)]);
        await client.PostIngestAsync([Payloads.Goal(match, sequence: 3, minute: 44, forHome: true)]);
        await client.PostIngestAsync([missing]);

        var stored = await ReadMatchAsync(match.Id);
        stored.HomeScore.ShouldBe(2);
        stored.AwayScore.ShouldBe(1);
        stored.LastEventSequence.ShouldBe(3);
    }

    [Fact]
    public async Task A_late_event_never_lowers_a_score_that_was_already_shown()
    {
        var match = await SeedMatchAsync();
        await using var app = FactoryWith(new StubMatchFeed());
        using var client = app.CreateClient();

        await client.PostIngestAsync([Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)]);
        await client.PostIngestAsync([Payloads.Goal(match, sequence: 3, minute: 44, forHome: true)]);
        var beforeRepair = await ReadMatchAsync(match.Id);

        await client.PostIngestAsync([Payloads.Goal(match, sequence: 2, minute: 31, forHome: false)]);

        var afterRepair = await ReadMatchAsync(match.Id);
        afterRepair.HomeScore.ShouldBe(beforeRepair.HomeScore);
        afterRepair.AwayScore.ShouldBe(beforeRepair.AwayScore + 1);
    }

    [Fact]
    public async Task A_sequence_jump_marks_the_match_degraded()
    {
        var match = await SeedMatchAsync();

        // The feed has nothing to give back, so the mark cannot be cleared by a repair racing this
        // assertion. What is under test is the detection, not the recovery.
        await using var app = FactoryWith(new StubMatchFeed());
        using var client = app.CreateClient();

        await client.PostIngestAsync([Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)]);
        await client.PostIngestAsync([Payloads.Goal(match, sequence: 4, minute: 44, forHome: true)]);

        (await ReadMatchAsync(match.Id)).IsDegraded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_contiguous_delivery_never_marks_the_match()
    {
        var match = await SeedMatchAsync();
        await using var app = FactoryWith(new StubMatchFeed());
        using var client = app.CreateClient();

        await client.PostIngestAsync([Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)]);
        await client.PostIngestAsync([Payloads.Goal(match, sequence: 2, minute: 31, forHome: false)]);

        (await ReadMatchAsync(match.Id)).IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task The_repair_asks_for_the_missing_range_and_clears_the_mark()
    {
        var match = await SeedMatchAsync();
        var missing = Payloads.Goal(match, sequence: 2, minute: 31, forHome: false);
        var feed = new StubMatchFeed(missing);
        await using var app = FactoryWith(feed);
        using var client = app.CreateClient();

        await client.PostIngestAsync([Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)]);
        await client.PostIngestAsync([Payloads.Goal(match, sequence: 3, minute: 44, forHome: true)]);

        await RepairAsync(app, match.Id, fromSequence: 2);

        var stored = await ReadMatchAsync(match.Id);
        stored.IsDegraded.ShouldBeFalse();
        stored.HomeScore.ShouldBe(2);
        stored.AwayScore.ShouldBe(1);

        // Asked for what was missing, not for the whole match. A repair that refetches from the first
        // event would pass a score assertion while doing far more work on every hiccup.
        feed.Requests.ShouldContain(request => request.MatchId == match.Id && request.FromSequence == 2);
    }

    [Fact]
    public async Task A_repair_that_comes_back_empty_leaves_the_match_degraded()
    {
        // Clearing the mark here would be a lie: the hole is still there, and the score on screen is
        // still missing whatever fell into it.
        var match = await SeedMatchAsync();
        await using var app = FactoryWith(new StubMatchFeed());
        using var client = app.CreateClient();

        await client.PostIngestAsync([Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)]);
        await client.PostIngestAsync([Payloads.Goal(match, sequence: 3, minute: 44, forHome: true)]);

        await RepairAsync(app, match.Id, fromSequence: 2);

        (await ReadMatchAsync(match.Id)).IsDegraded.ShouldBeTrue();
    }

    private PitchwireApiFactory FactoryWith(StubMatchFeed feed) =>
        new(postgres.ConnectionString, services =>
        {
            services.RemoveAll<IMatchFeed>();
            services.AddSingleton<IMatchFeed>(feed);
        });

    private static async Task RepairAsync(PitchwireApiFactory app, Guid matchId, int fromSequence)
    {
        using var scope = app.Services.CreateScope();
        var repairer = scope.ServiceProvider.GetRequiredService<GapRepairer>();

        await repairer.RepairAsync(new GapRepairRequest(matchId, fromSequence), TestContext.Current.CancellationToken);
    }

    private async Task<Match> SeedMatchAsync()
    {
        await using var db = postgres.CreateContext();
        return await Seed.MatchAsync(db);
    }

    private async Task<Match> ReadMatchAsync(Guid matchId)
    {
        await using var db = postgres.CreateContext();
        return await db.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId);
    }
}
