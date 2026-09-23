using System.Globalization;

namespace Pitchwire.Application.Reads;

/// <summary>
/// The names the read side caches under, and the tags that drop those entries again.
/// </summary>
/// <remarks>
/// One tag per season, rather than one per list. A goal changes the table, the scorer list, the form
/// of two teams and the fixture list all at once, and four tags to drop for one event is four chances
/// to forget one. The cost is a lower hit rate while matches are being played, which is the right
/// side to be wrong on: a stale table is a wrong table.
/// </remarks>
public static class CacheScope
{
    public static string SeasonTag(Guid seasonId) => $"season:{seasonId}";

    public static string Table(Guid seasonId) => $"table:{seasonId}";

    public static string Scorers(Guid seasonId, int top) =>
        string.Create(CultureInfo.InvariantCulture, $"scorers:{seasonId}:{top}");

    public static string Fixtures(Guid seasonId, int? round, Guid? teamId, int top, string? cursor) =>
        // Invariant throughout. A cache key that formats a number differently under another locale is
        // a key that stops matching itself, and the symptom is a cache that silently never hits.
        string.Create(
            CultureInfo.InvariantCulture,
            $"fixtures:{seasonId}:{round?.ToString(CultureInfo.InvariantCulture) ?? "all"}:{teamId?.ToString("N") ?? "all"}:{top}:{cursor ?? "start"}");

    public static string Results(Guid seasonId, int? round, Guid? teamId, int top, string? cursor) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"results:{seasonId}:{round?.ToString(CultureInfo.InvariantCulture) ?? "all"}:{teamId?.ToString("N") ?? "all"}:{top}:{cursor ?? "start"}");

    public static string Form(Guid teamId, Guid seasonId, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"form:{seasonId}:{teamId}:{count}");
}
