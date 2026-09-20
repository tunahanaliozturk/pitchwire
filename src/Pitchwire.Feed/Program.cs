var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// The simulator is a separate process with no access to the API database. It talks over HTTP like
// any other provider would, which is what keeps the ingestion boundary honest.
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

app.Run();

public partial class Program;
