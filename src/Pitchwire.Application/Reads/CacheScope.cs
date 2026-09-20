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

    public static string Scorers(Guid seasonId, int top) => $"scorers:{seasonId}:{top}";

    public static string Fixtures(Guid seasonId, int? round, int top, string? cursor) =>
        $"fixtures:{seasonId}:{round?.ToString() ?? "all"}:{top}:{cursor ?? "start"}";

    public static string Results(Guid seasonId, int top, string? cursor) =>
        $"results:{seasonId}:{top}:{cursor ?? "start"}";

    public static string Form(Guid teamId, Guid seasonId, int count) => $"form:{seasonId}:{teamId}:{count}";
}
