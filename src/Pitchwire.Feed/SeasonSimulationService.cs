using Microsoft.Extensions.Options;
using Pitchwire.Contracts;

namespace Pitchwire.Feed;

/// <summary>
/// Runs the season, one round at a time, for as long as the process lives.
/// </summary>
internal sealed partial class SeasonSimulationService(
    IServiceScopeFactory scopes,
    IHttpClientFactory clients,
    IOptions<FeedOptions> options,
    ILogger<SeasonSimulationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        await WaitForConsumerAsync(stoppingToken);

        // Every league, not just the one at the top of the list. A country picker in front of five
        // competitions that never play is a menu of empty rooms.
        var rounds = Catalogue.AllFixtures.GroupBy(f => f.Round).OrderBy(g => g.Key);

        foreach (var round in rounds)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var fixtures = round.ToList();
            RoundStarting(logger, round.Key, fixtures.Count);

            // Every match in a round is played at the same time, the way a league actually schedules
            // them. It is also what makes the per match row lock worth having.
            await Task.WhenAll(fixtures.Select(fixture => PlayAsync(fixture, stoppingToken)));

            await Task.Delay(TimeSpan.FromSeconds(settings.RoundIntervalSeconds), stoppingToken);
        }
    }

    private async Task PlayAsync(CatalogueFixture fixture, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<FeedDispatcher>();

        await dispatcher.PlayAsync(fixture, cancellationToken);
    }

    /// <summary>
    /// Waits until the API can answer, so the first round is not played into a service that has not
    /// finished migrating its database and would reject every event in it.
    /// </summary>
    private async Task WaitForConsumerAsync(CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(nameof(FeedDispatcher));

        for (var attempt = 1; attempt <= 60 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(new Uri("health/ready", UriKind.Relative), cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
#pragma warning disable CA1031 // Any failure here means the same thing: not ready yet.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Falls through to the wait below.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        ConsumerNeverAnswered(logger);
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information,
        Message = "Round {Round} kicking off with {Count} matches.")]
    private static partial void RoundStarting(ILogger logger, int round, int count);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning,
        Message = "The consumer did not become ready. Playing on anyway, and its ingestion will refuse what it cannot apply.")]
    private static partial void ConsumerNeverAnswered(ILogger logger);
}
