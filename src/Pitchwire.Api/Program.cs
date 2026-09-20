using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Pitchwire.Api.Ingestion;
using Pitchwire.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PitchwireDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
    // Snake case in the database, PascalCase in the model. The alternative is quoting identifiers in
    // every hand written query, which is a tax paid by whoever is debugging rather than writing.
    .UseSnakeCaseNamingConvention());

builder.Services.AddOptions<IngestOptions>()
    .Bind(builder.Configuration.GetSection(IngestOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Secret), "The ingestion secret is not configured.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<EventIngestor>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PitchwireDbContext>("postgres", tags: ["ready"]);

var app = builder.Build();

// Liveness answers whether the process is up. Readiness answers whether it can do its job, which
// here means reaching PostgreSQL. A readiness probe that always says yes is worse than none: it
// silences the one signal meant to warn you.
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

app.MapIngestion();

app.Run();

// The integration tests build this host through WebApplicationFactory, which needs the generated
// entry point to be visible from another assembly.
public partial class Program;
