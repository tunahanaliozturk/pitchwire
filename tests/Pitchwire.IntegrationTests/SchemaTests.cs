using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// What the database refuses, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// Idempotency is the whole reason this service can face a provider that retries. It is enforced by a
/// unique index rather than by application code, so the test that proves it has to reach the database.
/// </remarks>
public sealed class SchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task The_same_provider_event_cannot_be_stored_twice()
    {
        await using var db = postgres.CreateContext();
        var match = await Seed.MatchAsync(db);

        db.MatchEvents.Add(Seed.Event(match, sequence: 1, providerEventId: "evt-1"));
        await db.SaveChangesAsync();

        db.MatchEvents.Add(Seed.Event(match, sequence: 1, providerEventId: "evt-1"));

        var thrown = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        thrown.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe("23505");
    }

    [Fact]
    public async Task Two_providers_may_use_the_same_event_id()
    {
        // Providers number their events independently and nothing stops two of them choosing the same
        // string. If the key were the event id alone, adding a second provider would start silently
        // discarding its events as duplicates.
        await using var db = postgres.CreateContext();
        var match = await Seed.MatchAsync(db);

        db.MatchEvents.Add(Seed.Event(match, sequence: 1, providerEventId: "shared-id", provider: "simulator"));
        db.MatchEvents.Add(Seed.Event(match, sequence: 1, providerEventId: "shared-id", provider: "other-provider"));

        await Should.NotThrowAsync(() => db.SaveChangesAsync());

        var stored = await db.MatchEvents
            .Where(e => e.MatchId == match.Id && e.ProviderEventId == "shared-id")
            .CountAsync();

        stored.ShouldBe(2);
    }
}
