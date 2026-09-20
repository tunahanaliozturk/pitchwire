using Lib.Net.Http.WebPush;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pitchwire.Application.Ingestion;
using Pitchwire.Application.Notifications;
using Pitchwire.Application.Persistence;
using Pitchwire.Infrastructure.Ingestion;
using Pitchwire.Infrastructure.Notifications;
using Pitchwire.Infrastructure.Persistence;

namespace Pitchwire.Infrastructure;

/// <summary>
/// Everything that touches the outside world: the database, the provider, and the workers that run
/// on their own.
/// </summary>
public static class InfrastructureServices
{
    public static IServiceCollection AddPitchwireInfrastructure(
        this IServiceCollection services,
        string connectionString,
        Uri feedBaseAddress,
        string? redisConnectionString = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            // The second level. Without it the cache is still correct, it just starts cold after every
            // restart and cannot be shared, which is exactly what a test wants and a deployment does
            // not.
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
        }

        services.AddHybridCache();

        services.AddDbContext<PitchwireDbContext>(options => options
            .UseNpgsql(connectionString)
            // Snake case in the database, PascalCase in the model. The alternative is quoting
            // identifiers in every hand written query, which is a tax paid by whoever is debugging
            // rather than by whoever is writing.
            .UseSnakeCaseNamingConvention());

        // The application layer asks for the port and gets the same scoped instance, so a use case and
        // the host share one change tracker and one transaction.
        services.AddScoped<IPitchwireDbContext>(provider => provider.GetRequiredService<PitchwireDbContext>());
        services.AddSingleton<IStoreFailures, PostgresStoreFailures>();

        services.AddHttpClient<IMatchFeed, HttpMatchFeed>(client => client.BaseAddress = feedBaseAddress);

        services.AddSingleton<VapidKeys>();
        services.AddHttpClient<PushServiceClient>();
        services.AddScoped<IPushSender, WebPushSender>();

        services.AddHostedService<GapRepairService>();
        services.AddHostedService<NotificationRelayService>();
        services.AddHostedService<StaleMatchSweepService>();

        return services;
    }
}
