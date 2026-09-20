using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace Pitchwire.Application.Paging;

/// <summary>
/// The continuation token a client hands back to get the next page.
/// </summary>
/// <remarks>
/// It carries the key of the last row seen, so the next page is a seek rather than a count. An offset
/// would scan further on every page, and it would skip or repeat rows whenever the underlying set
/// changed between two requests, which a live fixture list does constantly.
/// <para>
/// It is opaque to the client and deliberately not signed. Nothing in it grants access to anything:
/// tampering with it produces a different page of the same public list, not a page somebody was not
/// allowed to see. A signature would add a key to manage in exchange for nothing.
/// </para>
/// <para>
/// The kind travels inside the token because a token from one list must not be accepted by another.
/// Decoded into the wrong shape, a fixture cursor would silently become a position nobody asked for.
/// </para>
/// </remarks>
public static class SkipToken
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Encode<TCursor>(string kind, TCursor cursor)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new TokenPayload<TCursor>(kind, cursor), Json);

        // Base64url, so the token survives a query string without being escaped into something a
        // reader would mistake for a bug.
        return Base64Url.EncodeToString(payload);
    }

    public static bool TryDecode<TCursor>(string? token, string kind, out TCursor cursor)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);

        cursor = default!;

        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            var payload = Base64Url.DecodeFromChars(token);
            var decoded = JsonSerializer.Deserialize<TokenPayload<TCursor>>(payload, Json);

            if (decoded is null || decoded.Value is null || !string.Equals(decoded.Kind, kind, StringComparison.Ordinal))
            {
                return false;
            }

            cursor = decoded.Value;
            return true;
        }
        catch (FormatException)
        {
            // Not base64url at all.
            return false;
        }
        catch (JsonException)
        {
            // Base64url of something that is not one of our tokens.
            return false;
        }
    }

    private sealed record TokenPayload<TCursor>(string Kind, TCursor Value);
}
