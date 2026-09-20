using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Pitchwire.Application.Persistence;

namespace Pitchwire.Application.Reads;

/// <summary>
/// The league table and the scorer list.
/// </summary>
/// <remarks>
/// Neither is paginated. A league table is one row per team and a scorer list is asked for as a top
/// handful, so both are bounded by the shape of the thing rather than by a limit somebody has to
/// remember to apply. Paginating them would add a cursor nobody would ever use a second page of.
/// </remarks>
public sealed class SeasonReads(IPitchwireDbContext db, HybridCache cache)
{
    private static readonly HybridCacheEntryOptions Tables = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    /// <summary>Every season this service holds, newest first.</summary>
    public async Task<IReadOnlyList<SeasonSummary>> SeasonsAsync(CancellationToken cancellationToken) =>
        await db.Seasons
            .AsNoTracking()
            .OrderByDescending(season => season.Year)
            .Select(season => new SeasonSummary(
                season.Id,
                season.Year,
                season.League!.Id,
                season.League.Name,
                season.League.Slug))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TableRow>> TableAsync(Guid seasonId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheScope.Table(seasonId),
            token => BuildTableAsync(seasonId, token),
            Tables,
            [CacheScope.SeasonTag(seasonId)],
            cancellationToken);

    public async Task<IReadOnlyList<ScorerRow>> ScorersAsync(Guid seasonId, int top, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheScope.Scorers(seasonId, top),
            token => BuildScorersAsync(seasonId, top, token),
            Tables,
            [CacheScope.SeasonTag(seasonId)],
            cancellationToken);

    private async ValueTask<IReadOnlyList<TableRow>> BuildTableAsync(Guid seasonId, CancellationToken cancellationToken)
    {
        var rows = await db.Standings
            .AsNoTracking()
            .Where(s => s.SeasonId == seasonId)
            .Select(s => new
            {
                Team = new TeamRef(s.Team!.Id, s.Team.Name, s.Team.ShortName, s.Team.Slug),
                s.Played,
                s.Won,
                s.Drawn,
                s.Lost,
                s.GoalsFor,
                s.GoalsAgainst,
                s.Points,
            })
            .ToListAsync(cancellationToken);

        // Ordered here rather than in SQL because goal difference is derived from two columns and
        // keeping it out of the table is what stops the two disagreeing. Twelve rows sort for free.
        var ordered = rows
            .OrderByDescending(row => row.Points)
            .ThenByDescending(row => row.GoalsFor - row.GoalsAgainst)
            .ThenByDescending(row => row.GoalsFor)
            .ThenBy(row => row.Team.Name, StringComparer.Ordinal)
            .ToList();

        return
        [
            .. ordered.Select((row, index) => new TableRow(
                index + 1,
                row.Team,
                row.Played,
                row.Won,
                row.Drawn,
                row.Lost,
                row.GoalsFor,
                row.GoalsAgainst,
                row.GoalsFor - row.GoalsAgainst,
                row.Points))
        ];
    }

    private async ValueTask<IReadOnlyList<ScorerRow>> BuildScorersAsync(Guid seasonId, int top, CancellationToken cancellationToken)
    {
        return await db.PlayerSeasonStats
            .AsNoTracking()
            .Where(s => s.SeasonId == seasonId && s.Goals > 0)
            .OrderByDescending(s => s.Goals)
            .ThenByDescending(s => s.Assists)
            .ThenBy(s => s.Player!.Name)
            .Take(top)
            .Select(s => new ScorerRow(
                s.PlayerId,
                s.Player!.Name,
                new TeamRef(s.Player.Team!.Id, s.Player.Team.Name, s.Player.Team.ShortName, s.Player.Team.Slug),
                s.Goals,
                s.Assists))
            .ToListAsync(cancellationToken);
    }
}
