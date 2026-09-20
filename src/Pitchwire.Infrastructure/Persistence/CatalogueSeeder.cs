using Microsoft.EntityFrameworkCore;
using Pitchwire.Contracts;
using Pitchwire.Domain;

namespace Pitchwire.Infrastructure.Persistence;

/// <summary>
/// Puts the league, its squads and its fixture list into the database.
/// </summary>
/// <remarks>
/// A real deployment imports this from the provider's fixture endpoint. Here both sides derive the
/// same identifiers from the shared catalogue, so the import is a local write and the demo starts
/// with a league already in place rather than an empty screen.
/// </remarks>
public static class CatalogueSeeder
{
    public static async Task EnsureAsync(PitchwireDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.Matches.AnyAsync(m => m.SeasonId == Catalogue.SeasonId, cancellationToken))
        {
            return;
        }

        db.Leagues.Add(new League
        {
            Id = Catalogue.LeagueId,
            Name = Catalogue.LeagueName,
            Country = Catalogue.LeagueCountry,
            Slug = Catalogue.LeagueSlug,
        });

        db.Seasons.Add(new Season
        {
            Id = Catalogue.SeasonId,
            LeagueId = Catalogue.LeagueId,
            Year = Catalogue.SeasonYear,
        });

        foreach (var team in Catalogue.Teams)
        {
            db.Teams.Add(new Team
            {
                Id = team.Id,
                Name = team.Name,
                ShortName = team.ShortName,
                Slug = team.Slug,
            });

            db.TeamSeasons.Add(new TeamSeason { SeasonId = Catalogue.SeasonId, TeamId = team.Id });
            db.Standings.Add(new Standing { SeasonId = Catalogue.SeasonId, TeamId = team.Id });

            foreach (var player in team.Players)
            {
                db.Players.Add(new Player { Id = player.Id, Name = player.Name, TeamId = team.Id });
            }
        }

        foreach (var fixture in Catalogue.Fixtures)
        {
            db.Matches.Add(new Match
            {
                Id = fixture.Id,
                SeasonId = Catalogue.SeasonId,
                Round = fixture.Round,
                KickoffUtc = fixture.KickoffUtc,
                HomeTeamId = fixture.Home.Id,
                AwayTeamId = fixture.Away.Id,
                Status = MatchStatus.Scheduled,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
