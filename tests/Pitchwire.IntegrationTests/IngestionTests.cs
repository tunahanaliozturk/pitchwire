using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pitchwire.Contracts;
using Pitchwire.Domain;
using Pitchwire.TestSupport;

namespace Pitchwire.IntegrationTests;

/// <summary>
/// What the ingestion endpoint does with a batch from a provider that retries.
/// </summary>
/// <remarks>
/// A provider redelivers, and it does so without asking. If a redelivery moved the score, the visible
/// result would be a 3-2 that should be 2-2, shown with total confidence. These tests are the reason
/// to believe that does not happen.
/// </remarks>
public sealed class IngestionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_goal_raises_the_score_and_is_reported_as_accepted()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        var match = await SeedMatchAsync();

        var response = await client.PostIngestAsync([Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)]);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.ReadIngestResponseAsync()).ShouldBe(new IngestResponse(Accepted: 1, Duplicate: 0, Rejected: 0));

        var stored = await ReadMatchAsync(match.Id);
        stored.HomeScore.ShouldBe(1);
        stored.AwayScore.ShouldBe(0);
        stored.Minute.ShouldBe(12);
        stored.LastEventSequence.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_event_sent_twice_is_reported_as_a_duplicate_and_the_score_holds()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        var match = await SeedMatchAsync();
        var goal = Payloads.Goal(match, sequence: 1, minute: 12, forHome: true);

        var first = await (await client.PostIngestAsync([goal])).ReadIngestResponseAsync();
        var second = await (await client.PostIngestAsync([goal])).ReadIngestResponseAsync();

        first.ShouldBe(new IngestResponse(Accepted: 1, Duplicate: 0, Rejected: 0));
        second.ShouldBe(new IngestResponse(Accepted: 0, Duplicate: 1, Rejected: 0));

        var stored = await ReadMatchAsync(match.Id);
        stored.HomeScore.ShouldBe(1);
        stored.LastEventSequence.ShouldBe(1);
    }

    [Fact]
    public async Task A_batch_holding_a_duplicate_still_stores_the_rest_of_it()
    {
        // A provider that pauses and then catches up resends what it already delivered alongside what
        // it has not. Abandoning the batch on the first duplicate would lose the new events every time.
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        var match = await SeedMatchAsync();
        var first = Payloads.Goal(match, sequence: 1, minute: 12, forHome: true);

        await client.PostIngestAsync([first]);
        var replay = await (await client.PostIngestAsync(
        [
            first,
            Payloads.Goal(match, sequence: 2, minute: 24, forHome: false),
            Payloads.Goal(match, sequence: 3, minute: 36, forHome: true),
        ])).ReadIngestResponseAsync();

        replay.ShouldBe(new IngestResponse(Accepted: 2, Duplicate: 1, Rejected: 0));

        var stored = await ReadMatchAsync(match.Id);
        stored.HomeScore.ShouldBe(2);
        stored.AwayScore.ShouldBe(1);
    }

    [Fact]
    public async Task An_invalid_signature_is_refused_and_writes_nothing()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        var match = await SeedMatchAsync();

        var response = await client.PostIngestAsync(
            [Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)],
            secret: "the-wrong-secret-entirely");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var stored = await ReadMatchAsync(match.Id);
        stored.HomeScore.ShouldBe(0);
        (await CountEventsAsync(match.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task A_stale_timestamp_is_refused()
    {
        // The signature is still valid here. What is not valid is its age, which is what stops a
        // captured batch being replayed tomorrow.
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        var match = await SeedMatchAsync();

        var response = await client.PostIngestAsync(
            [Payloads.Goal(match, sequence: 1, minute: 12, forHome: true)],
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-30));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CountEventsAsync(match.Id)).ShouldBe(0);
    }

    [Theory]
    [InlineData(1024 * 1024, HttpStatusCode.Accepted)]
    [InlineData(1024 * 1024 + 1, HttpStatusCode.RequestEntityTooLarge)]
    public async Task A_body_without_content_length_is_bounded_before_its_signature_is_checked(
        int bytes,
        HttpStatusCode expected)
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();

        // JSON permits trailing whitespace. The two bodies differ by only one byte, and both have a
        // valid signature. The content deliberately cannot tell HttpClient its length in advance.
        var body = new byte[bytes];
        var json = Encoding.UTF8.GetBytes("""{"events":[]}""");
        json.CopyTo(body, 0);
        Array.Fill(body, (byte)' ', json.Length, bytes - json.Length);

        var stamp = DateTimeOffset.UtcNow;
        using var content = new UnknownLengthContent(body);
        content.Headers.ContentType = new("application/json");
        content.Headers.ContentLength.ShouldBeNull();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ingest/events") { Content = content };
        request.Headers.TransferEncodingChunked = true;
        request.Headers.Add("X-Pitchwire-Signature", RequestSigner.Sign(PitchwireApiFactory.IngestSecret, stamp, body));
        request.Headers.Add("X-Pitchwire-Timestamp", stamp.ToStamp());

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Only_verified_ingest_batches_spend_the_feed_rate_allowance()
    {
        await using var app = new PitchwireApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string>
            {
                ["RateLimits:IngestBatchesPerWindow"] = "2",
                ["RateLimits:IngestWindowSeconds"] = "10",
            });
        using var client = app.CreateClient();

        using var invalid = await client.PostIngestAsync([], secret: "not-the-feed-secret");
        using var first = await client.PostIngestAsync([]);
        using var second = await client.PostIngestAsync([]);
        using var limited = await client.PostIngestAsync([]);

        invalid.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.Contains("Retry-After").ShouldBeTrue();

        using var health = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Events_for_two_matches_in_one_batch_both_land()
    {
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        var first = await SeedMatchAsync();
        var second = await SeedMatchAsync();

        var response = await (await client.PostIngestAsync(
        [
            Payloads.Goal(first, sequence: 1, minute: 10, forHome: true),
            Payloads.Goal(second, sequence: 1, minute: 15, forHome: false),
        ])).ReadIngestResponseAsync();

        response.Accepted.ShouldBe(2);
        (await ReadMatchAsync(first.Id)).HomeScore.ShouldBe(1);
        (await ReadMatchAsync(second.Id)).AwayScore.ShouldBe(1);
    }

    [Fact]
    public async Task An_event_for_an_unknown_match_is_rejected_rather_than_stored()
    {
        // A provider sending an event for a fixture this service does not have is a real case, and the
        // event cannot be applied to anything. It is counted so the provider can see it happened.
        await using var app = new PitchwireApiFactory(postgres.ConnectionString);
        using var client = app.CreateClient();
        var match = await SeedMatchAsync();
        var orphan = Payloads.Goal(match, sequence: 1, minute: 10, forHome: true) with { MatchId = Guid.NewGuid() };

        var response = await (await client.PostIngestAsync([orphan])).ReadIngestResponseAsync();

        response.ShouldBe(new IngestResponse(Accepted: 0, Duplicate: 0, Rejected: 1));
    }

    private async Task<Match> SeedMatchAsync()
    {
        await using var db = postgres.CreateContext();
        return await Seed.MatchAsync(db);
    }

    private async Task<Match> ReadMatchAsync(Guid matchId)
    {
        await using var db = postgres.CreateContext();
        return await db.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId);
    }

    private async Task<int> CountEventsAsync(Guid matchId)
    {
        await using var db = postgres.CreateContext();
        return await db.MatchEvents.CountAsync(e => e.MatchId == matchId);
    }

    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
