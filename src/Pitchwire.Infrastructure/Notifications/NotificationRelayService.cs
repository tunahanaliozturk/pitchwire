using Microsoft.Extensions.Options;
using Pitchwire.Application.Ingestion;
using Pitchwire.Application.Notifications;

namespace Pitchwire.Infrastructure.Notifications;

/// <summary>
/// Drains the notification outbox on a timer.
/// </summary>
/// <remarks>
/// Separate from the relay that does the work, so the delivery rules can be tested by calling them
/// rather than by starting a host and waiting.
/// </remarks>
internal sealed partial class NotificationRelayService(
    IServiceScopeFactory scopes,
    IOptions<IngestOptions> options,
    TimeProvider clock,
    ILogger<NotificationRelayService> logger) : BackgroundService
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RelayInterval, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var relay = scope.ServiceProvider.GetRequiredService<NotificationRelay>();

                await relay.DeliverAsync(BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed pass must not take the worker down with it.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                // The rows stay claimed by nothing and the next tick picks them up again.
                RelayFailed(logger, failure);
            }
        }
    }

    [LoggerMessage(EventId = 1302, Level = LogLevel.Error, Message = "A pass over the notification outbox failed.")]
    private static partial void RelayFailed(ILogger logger, Exception failure);
}
