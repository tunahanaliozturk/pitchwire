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

    /// <summary>
    /// Every country that has a league here, with how many.
    /// </summary>
    /// <remarks>
    /// The count is what lets a picker show a country that has something behind it and skip one that
    /// does not, without a second request per row to find out.
    /// </remarks>
    public async Task<IReadOnlyList<CountrySummary>> CountriesAsync(CancellationToken cancellationToken) =>
        // Filtered before the projection rather than after it. A predicate over a property of the
        // projected record has nothing to translate to in SQL, and the whole query fails at runtime
        // rather than at compile time.
        await db.Countries
            .AsNoTracking()
            .Where(country => db.Leagues.Any(league => league.CountryId == country.Id))
            .OrderBy(country => country.Name)
            .Select(country => new CountrySummary(
                country.Id,
                country.Name,
                country.Code,
                country.Slug,
                db.Leagues.Count(league => league.CountryId == country.Id)))
            .ToListAsync(cancellationToken);

    /// <summary>One country's leagues, top flight first.</summary>
    public async Task<IReadOnlyList<LeagueSummary>> LeaguesAsync(string countrySlug, CancellationToken cancellationToken) =>
        await db.Leagues
            .AsNoTracking()
            .Where(league => league.Country!.Slug == countrySlug)
            .OrderBy(league => league.Tier)
            .ThenBy(league => league.Name)
            .Select(league => new LeagueSummary(
                league.Id,
                league.Name,
                league.Slug,
                league.Tier,
                league.CountryId,
                league.Country!.Name,
                db.Seasons
                    .Where(season => season.LeagueId == league.Id)
                    .OrderByDescending(season => season.Year)
                    .Select(season => (Guid?)season.Id)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

    /// <summary>Every season this service holds, newest first.</summary>
    public async Task<IReadOnlyList<SeasonSummary>> SeasonsAsync(Guid? leagueId, CancellationToken cancellationToken) =>
        await db.Seasons
            .AsNoTracking()
            .Where(season => leagueId == null || season.LeagueId == leagueId)
            .OrderBy(season => season.League!.Tier)
            .ThenByDescending(season => season.Year)
            .Select(season => new SeasonSummary(
                season.Id,
                season.Year,
                season.League!.Id,
                season.League.Name,
                season.League.Slug))
            .ToListAsync(cancellationToken);

    /// <summary>The participants and scheduled rounds for one season.</summary>
    public async Task<SeasonDetail?> DetailAsync(Guid seasonId, CancellationToken cancellationToken)
    {
        var season = await db.Seasons
            .AsNoTracking()
            .Where(item => item.Id == seasonId)
            .Select(item => new
            {
                item.Id,
                item.Year,
                League = new LeagueRef(
                    item.League!.Id,
                    item.League.Name,
                    item.League.Slug,
                    item.League.Country!.Name),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (season is null)
        {
            return null;
        }

        var teams = await db.TeamSeasons
            .AsNoTracking()
            .Where(entry => entry.SeasonId == seasonId)
            .OrderBy(entry => entry.Team!.Name)
            .Select(entry => new TeamRef(
                entry.Team!.Id,
                entry.Team.Name,
                entry.Team.ShortName,
                entry.Team.Slug))
            .ToListAsync(cancellationToken);

        var rounds = await db.Matches
            .AsNoTracking()
            .Where(match => match.SeasonId == seasonId)
            .Select(match => match.Round)
            .Distinct()
            .OrderBy(round => round)
            .ToListAsync(cancellationToken);

        return new SeasonDetail(season.Id, season.Year, season.League, teams, rounds);
    }

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
