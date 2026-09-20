using Microsoft.Extensions.DependencyInjection;
using Pitchwire.Application.Ingestion;

namespace Pitchwire.Infrastructure.Ingestion;

/// <summary>
/// Drains the repair queue, one request at a time, for as long as the service runs.
/// </summary>
internal sealed partial class GapRepairService(
    GapRepairBacklog backlog,
    IServiceScopeFactory scopes,
    ILogger<GapRepairService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in backlog.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repairer = scope.ServiceProvider.GetRequiredService<GapRepairer>();

                await repairer.RepairAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // A failed repair must not take the worker down with it.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                // The match keeps its mark and the next gap on it queues another attempt. Retrying
                // here in a loop would hold the queue behind a provider that is already struggling.
                RepairFailed(logger, request.MatchId, failure);
            }
        }
    }

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = "Repair for match {MatchId} failed.")]
    private static partial void RepairFailed(ILogger logger, Guid matchId, Exception failure);
}
