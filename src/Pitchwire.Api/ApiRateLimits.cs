using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Pitchwire.Api.Devices;
using Pitchwire.Domain;

namespace Pitchwire.Api;

internal sealed class ApiRateLimitOptions
{
    public const string SectionName = "RateLimits";

    public int DeviceWritesPerMinute { get; set; } = 30;

    public int IngestBatchesPerWindow { get; set; } = 100;

    public int IngestWindowSeconds { get; set; } = 1;
}

internal static class ApiRateLimits
{
    public const string DeviceWrite = "device-write";
    public const string SignedIngest = "signed-ingest";

    public static IServiceCollection AddPitchwireRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiRateLimitOptions>()
            .Bind(configuration.GetSection(ApiRateLimitOptions.SectionName))
            .Validate(options => options.DeviceWritesPerMinute > 0 && options.IngestBatchesPerWindow > 0 &&
                options.IngestWindowSeconds > 0,
                "Rate limits must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejected, _) =>
            {
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    rejected.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                    title: "Rate limit exceeded.",
                    detail: "Try again after the current limit window.",
                    statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(rejected.HttpContext);
            };

            options.AddPolicy(DeviceWrite, context =>
            {
                // Middleware first resolves the bearer cookie to a real device. A cookie string by
                // itself is attacker-controlled and would give an unlimited supply of partitions.
                var device = context.Items[DeviceWriteIdentityMiddleware.ContextKey] as Device
                    ?? throw new InvalidOperationException("A device write reached the limiter without an identity.");
                var limit = context.RequestServices.GetRequiredService<IOptions<ApiRateLimitOptions>>()
                    .Value.DeviceWritesPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter(device.Id, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            options.AddPolicy(SignedIngest, context =>
            {
                var settings = context.RequestServices.GetRequiredService<IOptions<ApiRateLimitOptions>>().Value;

                // The HMAC middleware runs first, so only a verified provider spends this budget.
                return RateLimitPartition.GetFixedWindowLimiter(SignedIngest, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.IngestBatchesPerWindow,
                    Window = TimeSpan.FromSeconds(settings.IngestWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }
}
