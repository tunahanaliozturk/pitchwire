using System.Text;
using BenchmarkDotNet.Attributes;
using Pitchwire.Application.Paging;
using Pitchwire.Contracts;

namespace Pitchwire.Benchmarks;

/// <summary>
/// The two pieces of work that happen on the way in and on the way out of every request.
/// </summary>
/// <remarks>
/// Signature verification runs before an ingest request is believed, and the continuation token is
/// encoded on every page of every list. Neither is interesting on its own; both are interesting if
/// they turn out to cost more than the work they protect, which is the only reason to measure them.
/// </remarks>
[MemoryDiagnoser]
public class EdgeBenchmarks
{
    private const string Secret = "a-secret-long-enough-to-be-realistic-0123456789";

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private byte[] body = [];
    private string signature = "";
    private string timestamp = "";
    private string token = "";
    private (DateTimeOffset At, Guid Id) cursor;

    /// <summary>
    /// One event, a normal batch, and a catch up batch after a dropped connection.
    /// </summary>
    [Params(1, 20, 200)]
    public int EventsInBody { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var payload = new StringBuilder("{\"events\":[");

        for (var index = 0; index < EventsInBody; index++)
        {
            payload.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"{(index == 0 ? "" : ",")}{{\"sequence\":{index},\"minute\":{index % 95},\"kind\":\"Goal\"}}");
        }

        body = Encoding.UTF8.GetBytes(payload.Append("]}").ToString());
        signature = RequestSigner.Sign(Secret, Now, body);
        timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        cursor = (Now, Guid.NewGuid());
        token = SkipToken.Encode("fixtures", cursor);
    }

    [Benchmark]
    public string SignRequest() => RequestSigner.Sign(Secret, Now, body);

    [Benchmark]
    public SignatureVerdict VerifyRequest() =>
        RequestSigner.Verify(Secret, signature, timestamp, body, Now, Tolerance);

    [Benchmark]
    public string EncodeSkipToken() => SkipToken.Encode("fixtures", cursor);

    [Benchmark]
    public bool DecodeSkipToken() =>
        SkipToken.TryDecode<(DateTimeOffset At, Guid Id)>(token, "fixtures", out _);
}
