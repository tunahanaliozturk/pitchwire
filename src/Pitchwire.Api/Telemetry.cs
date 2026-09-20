using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pitchwire.Application.Telemetry;

namespace Pitchwire.Api;

internal static class Telemetry
{
    private const string ServiceName = "pitchwire.api";

    public static IHostApplicationBuilder AddPitchwireTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Exporting is opt in. Without an endpoint the OTLP exporter retries against localhost and
        // fills the log with connection failures, which teaches whoever reads it to ignore the log.
        var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var exporting = !string.IsNullOrWhiteSpace(endpoint);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(IngestionMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (exporting)
                {
                    metrics.AddOtlpExporter();
                }
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(IngestionMetrics.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (exporting)
                {
                    tracing.AddOtlpExporter();
                }
            });

        return builder;
    }
}
