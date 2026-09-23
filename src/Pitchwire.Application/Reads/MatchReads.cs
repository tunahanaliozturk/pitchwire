using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Pitchwire.Application.Paging;
using Pitchwire.Application.Persistence;
using Pitchwire.Domain;

namespace Pitchwire.Application.Reads;

/// <summary>
/// One page of matches, and where the next one starts.
/// </summary>
/// <remarks>
/// The cursor is returned rather than a link, because building a URL means knowing the request that
/// asked, and that belongs to the host rather than to a query.
/// </remarks>
public sealed record MatchPage(IReadOnlyList<MatchSummary> Matches, MatchCursor? Next);

/// <summary>
/// Everything a reader asks about matches.
/// </summary>
/// <remarks>
/// Every query projects straight into the shape it returns, with no tracking. Loading entities and
/// mapping them afterwards would fetch columns nobody reads and put every row into a change tracker
/// that exists to watch for writes this code will never make.
/// <para>
/// The live list and a single match are not cached. They change by the second, a client watching them
/// is already being pushed the changes, and a cache in front of them would only manufacture
/// disagreement between two screens looking at the same match.
/// </para>
/// </remarks>
public sealed class MatchReads(IPitchwireDbContext db, HybridCache cache)
{
    public const string FixturesToken = "fixtures";
    public const string ResultsToken = "results";
    public const string LiveToken = "live";

    private static readonly HybridCacheEntryOptions Lists = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    public Task<MatchPage> LiveAsync(int top, MatchCursor? after, CancellationToken cancellationToken)
    {
        var query = db.Matches
            .AsNoTracking()
            .Where(m => m.Status == MatchStatus.Live || m.Status == MatchStatus.Halftime);

        return AscendingPageAsync(query, top, after, cancellationToken);
    }

    public async Task<MatchPage> FixturesAsync(
        Guid seasonId,
        int? round,
        Guid? teamId,
        int top,
        MatchCursor? after,
        CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheScope.Fixtures(seasonId, round, teamId, top, Position(after)),
            async token =>
            {
                var query = db.Matches
                    .AsNoTracking()
                    .Where(m => m.SeasonId == seasonId && m.Status != MatchStatus.Finished);

                if (round is { } wanted)
                {
                    query = query.Where(m => m.Round == wanted);
                }

                if (teamId is { } team)
                {
                    query = query.Where(m => m.HomeTeamId == team || m.AwayTeamId == team);
                }

                return await AscendingPageAsync(query, top, after, token);
            },
            Lists,
            [CacheScope.SeasonTag(seasonId)],
            cancellationToken);

    public async Task<MatchPage> ResultsAsync(
        Guid seasonId,
        int? round,
        Guid? teamId,
        int top,
        MatchCursor? after,
        CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheScope.Results(seasonId, round, teamId, top, Position(after)),
            async token =>
            {
                // Newest first, because the question a results list answers is what happened last.
                var query = db.Matches
                    .AsNoTracking()
                    .Where(m => m.SeasonId == seasonId && m.Status == MatchStatus.Finished);

                if (round is { } wanted)
                {
                    query = query.Where(m => m.Round == wanted);
                }

                if (teamId is { } team)
                {
                    query = query.Where(m => m.HomeTeamId == team || m.AwayTeamId == team);
                }

                if (after is { } cursor)
                {
                    query = query.Where(m => m.KickoffUtc < cursor.KickoffUtc
                        || (m.KickoffUtc == cursor.KickoffUtc && m.Id.CompareTo(cursor.Id) < 0));
                }

                var page = await query
                    .OrderByDescending(m => m.KickoffUtc)
                    .ThenByDescending(m => m.Id)
                    .Take(top + 1)
                    .Select(Summary)
                    .ToListAsync(token);

                return Trim(page, top);
            },
            Lists,
            [CacheScope.SeasonTag(seasonId)],
            cancellationToken);

    public async Task<MatchDetail?> DetailAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);

        if (match is null)
        {
            return null;
        }

        var summary = await db.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId)
            .Select(Summary)
            .FirstAsync(cancellationToken);

        var events = await db.MatchEvents
            .AsNoTracking()
            .Where(e => e.MatchId == matchId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken);

        var lineup = await db.MatchLineups
            .AsNoTracking()
            .Where(entry => entry.MatchId == matchId)
            .ToListAsync(cancellationToken);

        var sheets = await db.MatchTeamSheets
            .AsNoTracking()
            .Where(sheet => sheet.MatchId == matchId)
            .ToListAsync(cancellationToken);

        // Every name the page shows, in one query. A lookup per event would be an N plus one on the
        // busiest screen in the application.
        var wanted = events
            .SelectMany(e => new[] { e.PlayerId, e.AssistPlayerId, e.ReplacedPlayerId })
            .Concat(lineup.Select(entry => (Guid?)entry.PlayerId))
            .OfType<Guid>()
            .Distinct()
            .ToList();

        var names = await db.Players
            .AsNoTracking()
            .Where(player => wanted.Contains(player.Id))
            .ToDictionaryAsync(player => player.Id, player => player.Name, cancellationToken);

        var statistics = await db.MatchStatistics
            .AsNoTracking()
            .Where(stats => stats.MatchId == matchId)
            .Select(stats => new TeamStatisticsView(
                stats.TeamId,
                stats.AsOfMinute,
                stats.Possession,
                stats.Shots,
                stats.ShotsOnTarget,
                stats.Corners,
                stats.Fouls,
                stats.Offsides))
            .ToListAsync(cancellationToken);

        var timeline = events
            .Select(e => new MatchEventView(
                e.Sequence,
                e.Minute,
                e.Kind.ToString(),
                e.TeamId,
                e.PlayerId is { } player && names.TryGetValue(player, out var scorer) ? scorer : null,
                e.AssistPlayerId is { } helper && names.TryGetValue(helper, out var assist) ? assist : null,
                e.ReplacedPlayerId is { } off && names.TryGetValue(off, out var leaving) ? leaving : null))
            .ToList();

        return new MatchDetail(
            summary,
            timeline,
            MatchSheetAssembly.Build(match, sheets, lineup, events, names),
            statistics);
    }

    /// <summary>
    /// The events of a match from a given sequence onwards.
    /// </summary>
    /// <remarks>
    /// What a client asks for after a dropped connection. A socket that reconnects has no idea what it
    /// missed, and without this it would either show a gap in the timeline or refetch a whole match to
    /// find the three events it did not see.
    /// </remarks>
    public async Task<IReadOnlyList<MatchEventView>> TimelineAsync(
        Guid matchId,
        int fromSequence,
        CancellationToken cancellationToken) =>
        await db.MatchEvents
            .AsNoTracking()
            .Where(e => e.MatchId == matchId && e.Sequence >= fromSequence)
            .OrderBy(e => e.Sequence)
            .Select(e => new MatchEventView(
                e.Sequence,
                e.Minute,
                e.Kind.ToString(),
                e.TeamId,
                db.Players.Where(p => p.Id == e.PlayerId).Select(p => p.Name).FirstOrDefault(),
                db.Players.Where(p => p.Id == e.AssistPlayerId).Select(p => p.Name).FirstOrDefault(),
                db.Players.Where(p => p.Id == e.ReplacedPlayerId).Select(p => p.Name).FirstOrDefault()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormEntry>> FormAsync(
        Guid teamId,
        Guid seasonId,
        int count,
        CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheScope.Form(teamId, seasonId, count),
            async token =>
            {
                var played = await db.Matches
                    .AsNoTracking()
                    .Where(m => m.SeasonId == seasonId
                        && m.Status == MatchStatus.Finished
                        && (m.HomeTeamId == teamId || m.AwayTeamId == teamId))
                    .OrderByDescending(m => m.KickoffUtc)
                    .Take(count)
                    .Select(m => new
                    {
                        m.Id,
                        m.KickoffUtc,
                        m.HomeTeamId,
                        m.HomeScore,
                        m.AwayScore,
                        Home = new TeamRef(m.HomeTeam!.Id, m.HomeTeam.Name, m.HomeTeam.ShortName, m.HomeTeam.Slug),
                        Away = new TeamRef(m.AwayTeam!.Id, m.AwayTeam.Name, m.AwayTeam.ShortName, m.AwayTeam.Slug),
                    })
                    .ToListAsync(token);

                IReadOnlyList<FormEntry> form =
                [
                    .. played.Select(m =>
                    {
                        var atHome = m.HomeTeamId == teamId;
                        var forGoals = atHome ? m.HomeScore : m.AwayScore;
                        var againstGoals = atHome ? m.AwayScore : m.HomeScore;

                        return new FormEntry(
                            m.Id,
                            m.KickoffUtc,
                            atHome ? m.Away : m.Home,
                            atHome,
                            forGoals,
                            againstGoals,
                            forGoals > againstGoals ? "Won" : forGoals == againstGoals ? "Drawn" : "Lost");
                    })
                ];

                return form;
            },
            Lists,
            [CacheScope.SeasonTag(seasonId)],
            cancellationToken);

    private static string? Position(MatchCursor? cursor) =>
        cursor is null
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{cursor.KickoffUtc:O}:{cursor.Id}");

    private static async Task<MatchPage> AscendingPageAsync(
        IQueryable<Match> query,
        int top,
        MatchCursor? after,
        CancellationToken cancellationToken)
    {
        if (after is { } cursor)
        {
            // The identifier breaks the tie. Two matches kicking off at the same minute is the normal
            // case in a league, and ordering on the time alone would drop or repeat one of them.
            query = query.Where(m =>
                m.KickoffUtc > cursor.KickoffUtc || (m.KickoffUtc == cursor.KickoffUtc && m.Id.CompareTo(cursor.Id) > 0));
        }

        // One more than asked for, which is how the page knows whether there is another without
        // running a second count query over the whole set.
        var page = await query
            .OrderBy(m => m.KickoffUtc)
            .ThenBy(m => m.Id)
            .Take(top + 1)
            .Select(Summary)
            .ToListAsync(cancellationToken);

        return Trim(page, top);
    }

    private static MatchPage Trim(List<MatchSummary> page, int top)
    {
        if (page.Count <= top)
        {
            return new MatchPage(page, Next: null);
        }

        var wanted = page[..top];
        var last = wanted[^1];

        return new MatchPage(wanted, new MatchCursor(last.KickoffUtc, last.Id));
    }

    private static Expression<Func<Match, MatchSummary>> Summary =>
        m => new MatchSummary(
            m.Id,
            m.Round,
            m.KickoffUtc,
            m.Status.ToString(),
            m.Minute,
            m.HomeScore,
            m.AwayScore,
            m.IsDegraded,
            new LeagueRef(
                m.Season!.League!.Id,
                m.Season.League.Name,
                m.Season.League.Slug,
                m.Season.League.Country!.Name),
            new TeamRef(m.HomeTeam!.Id, m.HomeTeam.Name, m.HomeTeam.ShortName, m.HomeTeam.Slug),
            new TeamRef(m.AwayTeam!.Id, m.AwayTeam.Name, m.AwayTeam.ShortName, m.AwayTeam.Slug));
}
