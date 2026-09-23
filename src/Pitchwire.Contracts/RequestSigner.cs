using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pitchwire.Contracts;

/// <summary>
/// Why a request was or was not accepted.
/// </summary>
/// <remarks>
/// The reason is reported as a value rather than a boolean so the caller can log which check failed
/// without the temptation to log the signature itself.
/// </remarks>
public enum SignatureVerdict
{
    Valid,
    Missing,
    Malformed,
    Expired,
    Mismatch,
}

/// <summary>
/// HMAC-SHA256 over the timestamp and the request body, in the shape webhook providers use.
/// </summary>
/// <remarks>
/// The timestamp is inside the signed material rather than beside it. A signature that covers only the
/// body can be lifted from a captured request and replayed into any later window by rewriting one
/// header, and the replay window then protects nothing.
/// </remarks>
public static class RequestSigner
{
    private const int MacLength = 32;

    public static string Sign(string secret, DateTimeOffset timestamp, ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        Span<byte> mac = stackalloc byte[MacLength];
        ComputeMac(secret, timestamp.ToUnixTimeSeconds(), body, mac);
        return Convert.ToBase64String(mac);
    }

    public static SignatureVerdict Verify(
        string secret,
        string? signatureHeader,
        string? timestampHeader,
        ReadOnlySpan<byte> body,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(timestampHeader))
        {
            return SignatureVerdict.Missing;
        }

        if (!long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return SignatureVerdict.Malformed;
        }

        if (unixSeconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() ||
            unixSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return SignatureVerdict.Malformed;
        }

        Span<byte> provided = stackalloc byte[MacLength];
        if (!Convert.TryFromBase64String(signatureHeader, provided, out var decoded) || decoded != MacLength)
        {
            // A short signature is refused outright. Comparing only the bytes that arrived would let a
            // single byte signature pass once in every 256 attempts.
            return SignatureVerdict.Malformed;
        }

        var age = now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (age > tolerance || age < -tolerance)
        {
            return SignatureVerdict.Expired;
        }

        Span<byte> expected = stackalloc byte[MacLength];
        ComputeMac(secret, unixSeconds, body, expected);

        return CryptographicOperations.FixedTimeEquals(expected, provided)
            ? SignatureVerdict.Valid
            : SignatureVerdict.Mismatch;
    }

    private static void ComputeMac(string secret, long unixSeconds, ReadOnlySpan<byte> body, Span<byte> destination)
    {
        var key = Encoding.UTF8.GetBytes(secret);

        // The timestamp goes in as eight fixed width bytes. Writing it as text and concatenating would
        // let the pair (12, "abc") and the pair (1, "2abc") sign the same string.
        var material = new byte[sizeof(long) + body.Length];
        BinaryPrimitives.WriteInt64BigEndian(material, unixSeconds);
        body.CopyTo(material.AsSpan(sizeof(long)));

        HMACSHA256.HashData(key, material, destination);
    }
}
