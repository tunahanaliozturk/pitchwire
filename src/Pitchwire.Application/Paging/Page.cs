namespace Pitchwire.Application.Paging;

/// <summary>
/// One page of a collection, in the shape Microsoft's REST APIs use.
/// </summary>
/// <remarks>
/// The envelope carries a link rather than a token because the client should not have to know how to
/// build the next request. On the last page there is no link at all: an empty string would make a
/// caller check the value as well as its presence, and one of the two checks always gets forgotten.
/// </remarks>
public sealed record Page<T>(IReadOnlyList<T> Value, string? NextLink);

/// <summary>
/// How far into a list a request has read.
/// </summary>
public sealed record MatchCursor(DateTimeOffset KickoffUtc, Guid Id);

/// <summary>
/// The paging arguments every collection endpoint takes.
/// </summary>
/// <remarks>
/// The cap is not politeness. Without it a client can ask for the whole season in one request, and
/// the slowest query in the service becomes whatever somebody typed into a URL.
/// </remarks>
public static class PageSize
{
    public const int Default = 50;
    public const int Max = 100;

    public static int Clamp(int? requested) => requested switch
    {
        null or <= 0 => Default,
        > Max => Max,
        _ => requested.Value,
    };
}
