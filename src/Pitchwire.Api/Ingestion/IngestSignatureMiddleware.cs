using System.Buffers;
using Microsoft.Extensions.Options;
using Pitchwire.Application.Ingestion;
using Pitchwire.Contracts;

namespace Pitchwire.Api.Ingestion;

/// <summary>
/// Checks the provider's signature before anything else touches the request.
/// </summary>
/// <remarks>
/// This runs as middleware rather than as an endpoint filter because the signature covers the raw
/// body, and by the time a filter runs the body has already been read and deserialised. Buffering
/// here, before binding, is what lets the same bytes be verified and then parsed.
/// </remarks>
internal sealed partial class IngestSignatureMiddleware(
    RequestDelegate next,
    IOptions<IngestOptions> options,
    TimeProvider clock,
    ILogger<IngestSignatureMiddleware> logger)
{
    public const string SignatureHeader = "X-Pitchwire-Signature";
    public const string TimestampHeader = "X-Pitchwire-Timestamp";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = options.Value;
        var request = context.Request;

        if (request.ContentLength > settings.MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        request.EnableBuffering();

        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(32 * 1024);

        try
        {
            int read;

            while ((read = await request.Body.ReadAsync(chunk.AsMemory(), context.RequestAborted)) != 0)
            {
                // A missing or dishonest Content-Length must not turn signature verification into
                // an unbounded allocation. Read no more than one chunk beyond the configured cap.
                if (buffer.Length + read > settings.MaxBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        request.Body.Position = 0;

        var verdict = RequestSigner.Verify(
            settings.Secret,
            request.Headers[SignatureHeader],
            request.Headers[TimestampHeader],
            buffer.GetBuffer().AsSpan(0, (int)buffer.Length),
            clock.GetUtcNow(),
            settings.ReplayTolerance);

        if (verdict != SignatureVerdict.Valid)
        {
            // The verdict is logged, the signature is not. A rejected signature in a log file is a
            // rejected signature an attacker can read back.
            RejectedRequest(logger, verdict);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Warning, Message = "Refused an ingestion request: {Verdict}.")]
    private static partial void RejectedRequest(ILogger logger, SignatureVerdict verdict);
}
