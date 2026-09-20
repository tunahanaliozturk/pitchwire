using System.Net;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// What the health endpoints say, and when they refuse to say it.
/// </summary>
/// <remarks>
/// A readiness probe that answers healthy whatever the state of its dependencies is worse than having
/// none: an orchestrator will keep routing traffic to a service that cannot serve it, and the one
/// signal meant to catch that has been silenced. This is the test that the probe can actually fail.
/// </remarks>
public sealed class HealthTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Readiness_passes_when_the_database_is_reachable()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_fails_when_the_database_cannot_be_reached()
    {
        // Port 1 is not a database. The check has to notice rather than report on the process alone.
        await using var app = new PitchwireApiFactory(
            "Host=127.0.0.1;Port=1;Database=pitchwire;Username=pitchwire;Password=pitchwire;Timeout=1");
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Liveness_answers_even_when_the_database_is_gone()
    {
        // Liveness is about the process. Tying it to a dependency means a database blip restarts every
        // instance at once, which turns a recoverable outage into a longer one.
        await using var app = new PitchwireApiFactory(
            "Host=127.0.0.1;Port=1;Database=pitchwire;Username=pitchwire;Password=pitchwire;Timeout=1");
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
