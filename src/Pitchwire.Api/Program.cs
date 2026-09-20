var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness only for now. A readiness check that proves PostgreSQL is reachable arrives with the
// DbContext, because until there is a database there is nothing to be ready for, and a readiness
// probe that always answers healthy is worse than none.
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

app.Run();

// The integration tests build this host through WebApplicationFactory, which needs the generated
// entry point to be visible from another assembly.
public partial class Program;
