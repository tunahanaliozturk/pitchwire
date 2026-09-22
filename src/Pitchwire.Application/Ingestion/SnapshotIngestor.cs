using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Persistence;
using Pitchwire.Contracts;
using Pitchwire.Domain;

namespace Pitchwire.Application.Ingestion;

/// <summary>
/// Takes the parts of a match that are not events: the team sheets and the statistics.
/// </summary>
/// <remarks>
/// Neither is an event, so neither carries a sequence, and the ordering rule is different. A team
/// sheet replaces whatever was there, and a statistics snapshot is cumulative, so one that arrives
/// after a later one has already been stored is older news and is dropped. That makes both of them
/// self healing: a lost snapshot needs nobody to ask for it, because the next one contains it.
/// </remarks>
public sealed class SnapshotIngestor(IPitchwireDbContext db)
{
    public async Task<(int Lineups, int Statistics)> StoreAsync(
        IReadOnlyList<LineupPayload> lineups,
        IReadOnlyList<StatisticsPayload> statistics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lineups);
        ArgumentNullException.ThrowIfNull(statistics);

        var storedLineups = await StoreLineupsAsync(lineups, cancellationToken);
        var storedStatistics = await StoreStatisticsAsync(statistics, cancellationToken);

        if (storedLineups > 0 || storedStatistics > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return (storedLineups, storedStatistics);
    }

    private async Task<int> StoreLineupsAsync(IReadOnlyList<LineupPayload> lineups, CancellationToken cancellationToken)
    {
        var stored = 0;

        foreach (var lineup in lineups)
        {
            // A team sheet for a fixture this service does not have cannot be shown against anything.
            if (!await db.Matches.AnyAsync(m => m.Id == lineup.MatchId, cancellationToken))
            {
                continue;
            }

            var sheet = await db.MatchTeamSheets
                .FirstOrDefaultAsync(s => s.MatchId == lineup.MatchId && s.TeamId == lineup.TeamId, cancellationToken);

            if (sheet is null)
            {
                db.MatchTeamSheets.Add(new MatchTeamSheet
                {
                    MatchId = lineup.MatchId,
                    TeamId = lineup.TeamId,
                    Formation = lineup.Formation,
                });
            }
            else
            {
                sheet.Formation = lineup.Formation;
            }

            // Replaced rather than merged. A sheet is the whole story of who is named, and a player
            // dropped from a corrected one has to disappear rather than linger from the first attempt.
            var existing = await db.MatchLineups
                .Where(entry => entry.MatchId == lineup.MatchId && entry.TeamId == lineup.TeamId)
                .ToListAsync(cancellationToken);

            db.MatchLineups.RemoveRange(existing);

            foreach (var player in lineup.Players)
            {
                db.MatchLineups.Add(new MatchLineupEntry
                {
                    MatchId = lineup.MatchId,
                    TeamId = lineup.TeamId,
                    PlayerId = player.PlayerId,
                    ShirtNumber = player.ShirtNumber,
                    Position = ToDomain(player.Position),
                    IsStarter = player.Starter,
                });
            }

            stored++;
        }

        return stored;
    }

    private async Task<int> StoreStatisticsAsync(IReadOnlyList<StatisticsPayload> statistics, CancellationToken cancellationToken)
    {
        var stored = 0;

        foreach (var snapshot in statistics)
        {
            var current = await db.MatchStatistics
                .FirstOrDefaultAsync(s => s.MatchId == snapshot.MatchId && s.TeamId == snapshot.TeamId, cancellationToken);

            if (current is null)
            {
                if (!await db.Matches.AnyAsync(m => m.Id == snapshot.MatchId, cancellationToken))
                {
                    continue;
                }

                db.MatchStatistics.Add(new MatchStatistics
                {
                    MatchId = snapshot.MatchId,
                    TeamId = snapshot.TeamId,
                    AsOfMinute = snapshot.AsOfMinute,
                    Possession = snapshot.Possession,
                    Shots = snapshot.Shots,
                    ShotsOnTarget = snapshot.ShotsOnTarget,
                    Corners = snapshot.Corners,
                    Fouls = snapshot.Fouls,
                    Offsides = snapshot.Offsides,
                });

                stored++;
                continue;
            }

            if (snapshot.AsOfMinute < current.AsOfMinute)
            {
                // Older news. Writing it would walk the shot count backwards, which is the statistics
                // version of a score going down.
                continue;
            }

            current.AsOfMinute = snapshot.AsOfMinute;
            current.Possession = snapshot.Possession;
            current.Shots = snapshot.Shots;
            current.ShotsOnTarget = snapshot.ShotsOnTarget;
            current.Corners = snapshot.Corners;
            current.Fouls = snapshot.Fouls;
            current.Offsides = snapshot.Offsides;
            stored++;
        }

        return stored;
    }

    private static Position ToDomain(LineupPosition position) => position switch
    {
        LineupPosition.Goalkeeper => Position.Goalkeeper,
        LineupPosition.Defender => Position.Defender,
        LineupPosition.Midfielder => Position.Midfielder,
        _ => Position.Forward,
    };
}
