using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Pitchwire.Api;
using Pitchwire.Api.Devices;
using Pitchwire.Api.Ingestion;
using Pitchwire.Api.Live;
using Pitchwire.Api.Reads;
using Pitchwire.Application;
using Pitchwire.Application.Ingestion;
using Pitchwire.Application.Live;
using Pitchwire.Infrastructure;
using Pitchwire.Infrastructure.Notifications;
using Pitchwire.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<IngestOptions>()
    .Bind(builder.Configuration.GetSection(IngestOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Secret), "The ingestion secret is not configured.")
    .ValidateOnStart();

// Read once, before the container is built, because the provider address decides how the typed HTTP
// client is configured rather than being read on every call.
var ingestSettings = builder.Configuration.GetSection(IngestOptions.SectionName).Get<IngestOptions>()
    ?? new IngestOptions();

builder.Services.AddSignalR();
builder.Services.AddSingleton<ILiveUpdates, SignalRLiveUpdates>();
builder.Services.AddOpenApi(OpenApiSchemas.NarrowNumbersToNumbers);
builder.AddPitchwireTelemetry();

builder.Services.AddOptions<WebPushOptions>()
    .Bind(builder.Configuration.GetSection(WebPushOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPitchwireApplication();
builder.Services.AddPitchwireInfrastructure(
    builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("The Postgres connection string is not configured."),
    ingestSettings.FeedBaseAddress,
    builder.Configuration.GetConnectionString("Redis"));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PitchwireDbContext>("postgres", tags: ["ready"]);

var app = builder.Build();

// Migrations and the catalogue are applied here only when the deployment asks for it. A service that
// migrates its own database on every start is convenient in a demo and a hazard anywhere else, so the
// switch is off unless compose or a developer turns it on.
if (app.Configuration.GetValue("Seed:Enabled", false))
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<PitchwireDbContext>();

    await database.Database.MigrateAsync();
    await CatalogueSeeder.EnsureAsync(database, CancellationToken.None);
}

// Liveness answers whether the process is up. Readiness answers whether it can do its job, which here
// means reaching PostgreSQL. A readiness probe that always says yes is worse than none: it silences
// the one signal meant to warn you.
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

// Only the ingestion path is signed. Applying this globally would demand a provider signature on the
// health endpoints, which are read by a container runtime that has no secret.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/ingest"),
    ingest => ingest.UseMiddleware<IngestSignatureMiddleware>());

app.MapDevices();
app.MapIngestion();
app.MapReads();

// The document is served in every environment on purpose. A public read API whose shape is only
// documented on a developer machine is an API nobody outside can use.
app.MapHub<MatchHub>("/hub/matches");
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();

// The integration tests build this host through WebApplicationFactory, which needs the generated entry
// point to be visible from another assembly.
public partial class Program;
