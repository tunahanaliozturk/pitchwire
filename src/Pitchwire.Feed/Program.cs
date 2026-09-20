using Microsoft.Extensions.Options;
using Pitchwire.Feed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<FeedOptions>()
    .Bind(builder.Configuration.GetSection(FeedOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Secret), "The feed secret is not configured.")
    .Validate(options => options.ClockFactor > 0, "The clock factor has to be greater than zero.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FeedLedger>();
builder.Services.AddHostedService<SeasonSimulationService>();

builder.Services.AddHttpClient<FeedDispatcher>(nameof(FeedDispatcher), (services, client) =>
    client.BaseAddress = services.GetRequiredService<IOptions<FeedOptions>>().Value.ApiBaseAddress);

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapFeed();

app.Run();
