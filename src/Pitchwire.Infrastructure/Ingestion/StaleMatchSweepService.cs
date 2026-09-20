using Microsoft.Extensions.Options;
using Pitchwire.Application.Ingestion;

namespace Pitchwire.Infrastructure.Ingestion;

/// <summary>
/// Runs the quiet match sweep on a timer.
/// </summary>
internal sealed partial class StaleMatchSweepService(
    IServiceScopeFactory scopes,
    IOptions<IngestOptions> options,
    TimeProvider clock,
    ILogger<StaleMatchSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<StaleMatchSweeper>();

                await sweeper.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed sweep must not take the worker down with it.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                // The next tick tries again. A provider that is down stays down for a few seconds.
                SweepFailed(logger, failure);
            }
        }
    }

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "A sweep for quiet matches failed.")]
    private static partial void SweepFailed(ILogger logger, Exception failure);
}
