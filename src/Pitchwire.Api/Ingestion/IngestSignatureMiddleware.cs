using Microsoft.Extensions.Options;
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
        await request.Body.CopyToAsync(buffer, context.RequestAborted);
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
