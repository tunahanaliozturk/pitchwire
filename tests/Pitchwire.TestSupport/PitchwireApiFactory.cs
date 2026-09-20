using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pitchwire.Contracts;

namespace Pitchwire.TestSupport;

/// <summary>
/// The API host, wired to a throwaway PostgreSQL instance and a known ingestion secret.
/// </summary>
public sealed class PitchwireApiFactory(
    string connectionString,
    Action<IServiceCollection>? configureServices = null,
    string? redisConnectionString = null)
    : WebApplicationFactory<Program>
{
    public const string IngestSecret = "integration-test-secret-long-enough";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:Postgres", connectionString);

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            builder.UseSetting("ConnectionStrings:Redis", redisConnectionString);
        }
        builder.UseSetting("Ingest:Secret", IngestSecret);

        // A test has no patience for the half minute of silence that marks a match quiet in a real
        // deployment, and waiting it out would only prove that Task.Delay works.
        builder.UseSetting("Ingest:StaleAfter", "00:00:00");

        // Several hosts come up in one process across a suite. The Windows event log provider poisons
        // later log writes once the first host disposes it, so no host here keeps a provider.
        builder.ConfigureLogging(logging => logging.ClearProviders());

        // Runs after the application's own registrations, so a test can replace a real dependency
        // with a double it controls.
        builder.ConfigureServices(services => configureServices?.Invoke(services));
    }
}

/// <summary>
/// Posting a batch the way the provider does, signature and all.
/// </summary>
public static class IngestClientExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseMessage> PostIngestAsync(
        this HttpClient client,
        IReadOnlyList<MatchEventPayload> events,
        string secret = PitchwireApiFactory.IngestSecret,
        DateTimeOffset? timestamp = null,
        string? overrideSignature = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var stamp = timestamp ?? DateTimeOffset.UtcNow;
        var body = JsonSerializer.SerializeToUtf8Bytes(new IngestRequest(events), Json);

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ingest/events") { Content = content };
        request.Headers.Add("X-Pitchwire-Signature", overrideSignature ?? RequestSigner.Sign(secret, stamp, body));
        request.Headers.Add("X-Pitchwire-Timestamp", stamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

        return await client.SendAsync(request);
    }

    public static async Task<IngestResponse> ReadIngestResponseAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return await response.Content.ReadFromJsonAsync<IngestResponse>(Json)
            ?? throw new InvalidOperationException("The ingestion endpoint returned an empty body.");
    }

    public static string ToStamp(this DateTimeOffset moment) =>
        moment.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
}
