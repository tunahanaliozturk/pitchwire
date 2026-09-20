using Microsoft.Extensions.DependencyInjection;
using Pitchwire.Application.Ingestion;
using Pitchwire.Application.Notifications;
using Pitchwire.Application.Projections;
using Pitchwire.Application.Reads;
using Pitchwire.Application.Telemetry;

namespace Pitchwire.Application;

/// <summary>
/// What the application layer offers, registered in one place.
/// </summary>
/// <remarks>
/// Configuration is not bound here. Reading settings is the host's job, and a layer that reaches into
/// configuration for itself is a layer that cannot be composed differently by a different host.
/// </remarks>
public static class ApplicationServices
{
    public static IServiceCollection AddPitchwireApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMetrics();
        services.AddSingleton<IngestionMetrics>();
        services.AddSingleton<GapRepairBacklog>();

        services.AddScoped<EventIngestor>();
        services.AddScoped<SeasonProjector>();
        services.AddScoped<DeviceRegistry>();
        services.AddScoped<NotificationFanout>();
        services.AddScoped<NotificationRelay>();
        services.AddScoped<MatchReads>();
        services.AddScoped<SeasonReads>();
        services.AddScoped<GapRepairer>();
        services.AddScoped<StaleMatchSweeper>();

        return services;
    }
}
