using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Persistence;
using Pitchwire.Domain;

namespace Pitchwire.Application.Projections;

/// <summary>
/// Rebuilds a season's league table and player totals from what the match log already says.
/// </summary>
/// <remarks>
/// Rebuilt rather than adjusted. An incremental table has to be right about every edge: a match that
/// finishes twice, a late goal that changes a result after the points were awarded, a rebuild that
/// moves a score after the fact. Each of those is a chance to double count, and a wrong table is the
/// kind of wrong nobody notices until somebody adds up the points by hand.
/// <para>
/// Recomputing a season is one pass over its finished matches. At a league's size that is a few
/// hundred rows and it runs inside the ingestion transaction. If a season ever grows past what that
/// can carry, the upgrade is to keep a per match contribution and apply the difference, measured
/// before it is written.
/// </para>
/// </remarks>
public sealed class SeasonProjector(IPitchwireDbContext db)
{
    public async Task ProjectAsync(Guid seasonId, CancellationToken cancellationToken)
    {
        await ProjectStandingsAsync(seasonId, cancellationToken);
        await ProjectPlayerTotalsAsync(seasonId, cancellationToken);
    }

    private async Task ProjectStandingsAsync(Guid seasonId, CancellationToken cancellationToken)
    {
        var finished = await db.Matches
            .AsNoTracking()
            .Where(m => m.SeasonId == seasonId && m.Status == MatchStatus.Finished)
            .Select(m => new { m.HomeTeamId, m.AwayTeamId, m.HomeScore, m.AwayScore })
            .ToListAsync(cancellationToken);

        var rows = await db.Standings
            .Where(s => s.SeasonId == seasonId)
            .ToDictionaryAsync(s => s.TeamId, cancellationToken);

        foreach (var row in rows.Values)
        {
            Reset(row);
        }

        foreach (var match in finished)
        {
            // A match whose teams have no row yet belongs to a season this service has not been told
            // about properly. Skipping it keeps the table honest about the teams it does know.
            if (!rows.TryGetValue(match.HomeTeamId, out var home) || !rows.TryGetValue(match.AwayTeamId, out var away))
            {
                continue;
            }

            StandingsMath.ApplyFinishedMatch(home, away, match.HomeScore, match.AwayScore);
        }
    }

    private async Task ProjectPlayerTotalsAsync(Guid seasonId, CancellationToken cancellationToken)
    {
        var events = await db.MatchEvents
            .AsNoTracking()
            .Where(e => db.Matches.Any(m => m.Id == e.MatchId && m.SeasonId == seasonId))
            .Select(e => new { e.Kind, e.PlayerId, e.AssistPlayerId })
            .ToListAsync(cancellationToken);

        var rows = await db.PlayerSeasonStats
            .Where(s => s.SeasonId == seasonId)
            .ToDictionaryAsync(s => s.PlayerId, cancellationToken);

        foreach (var row in rows.Values)
        {
            row.Goals = 0;
            row.Assists = 0;
            row.YellowCards = 0;
            row.RedCards = 0;
        }

        foreach (var @event in events)
        {
            if (@event.PlayerId is { } playerId)
            {
                var row = Row(rows, seasonId, playerId);

                switch (@event.Kind)
                {
                    case MatchEventKind.Goal:
                    case MatchEventKind.PenaltyGoal:
                        row.Goals++;
                        break;

                    case MatchEventKind.Yellow:
                        row.YellowCards++;
                        break;

                    case MatchEventKind.Red:
                        row.RedCards++;
                        break;

                    default:
                        // An own goal is not a goal for the player who put it in, and a substitution
                        // is not a statistic anybody asks a top scorer list for.
                        break;
                }
            }

            if (@event is { Kind: MatchEventKind.Goal or MatchEventKind.PenaltyGoal, AssistPlayerId: { } assistId })
            {
                Row(rows, seasonId, assistId).Assists++;
            }
        }
    }

    private PlayerSeasonStats Row(Dictionary<Guid, PlayerSeasonStats> rows, Guid seasonId, Guid playerId)
    {
        if (rows.TryGetValue(playerId, out var existing))
        {
            return existing;
        }

        var created = new PlayerSeasonStats { SeasonId = seasonId, PlayerId = playerId };
        db.PlayerSeasonStats.Add(created);
        rows[playerId] = created;

        return created;
    }

    private static void Reset(Standing row)
    {
        row.Played = 0;
        row.Won = 0;
        row.Drawn = 0;
        row.Lost = 0;
        row.GoalsFor = 0;
        row.GoalsAgainst = 0;
        row.Points = 0;
    }
}
