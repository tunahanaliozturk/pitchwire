using Microsoft.EntityFrameworkCore;
using Pitchwire.Contracts;
using Pitchwire.Domain;

namespace Pitchwire.Infrastructure.Persistence;

/// <summary>
/// Puts the countries, their leagues, the squads and the fixture lists into the database.
/// </summary>
/// <remarks>
/// A real deployment imports this from the provider's fixture endpoint. Here both sides derive the
/// same identifiers from the shared catalogue, so the import is a local write and the demo starts
/// with leagues already in place rather than an empty screen.
/// <para>
/// Each league is seeded on its own, so a database that already holds the first league from an
/// earlier version gains the others without anything that was there being written twice.
/// </para>
/// </remarks>
public static class CatalogueSeeder
{
    public static async Task EnsureAsync(PitchwireDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var knownCountries = await db.Countries.Select(c => c.Id).ToListAsync(cancellationToken);

        foreach (var country in Catalogue.Countries.Where(c => !knownCountries.Contains(c.Id)))
        {
            db.Countries.Add(new Country
            {
                Id = country.Id,
                Name = country.Name,
                Code = country.Code,
                Slug = country.Slug,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var league in Catalogue.Leagues)
        {
            if (await db.Matches.AnyAsync(m => m.SeasonId == league.SeasonId, cancellationToken))
            {
                continue;
            }

            await SeedLeagueAsync(db, league, cancellationToken);
        }
    }

    private static async Task SeedLeagueAsync(PitchwireDbContext db, CatalogueLeague league, CancellationToken cancellationToken)
    {
        if (!await db.Leagues.AnyAsync(l => l.Id == league.Id, cancellationToken))
        {
            db.Leagues.Add(new League
            {
                Id = league.Id,
                Name = league.Name,
                Slug = league.Slug,
                Tier = league.Tier,
                CountryId = league.Country.Id,
            });
        }

        if (!await db.Seasons.AnyAsync(s => s.Id == league.SeasonId, cancellationToken))
        {
            db.Seasons.Add(new Season
            {
                Id = league.SeasonId,
                LeagueId = league.Id,
                Year = Catalogue.SeasonYear,
            });
        }

        var knownTeams = await db.Teams.Select(t => t.Id).ToListAsync(cancellationToken);
        var knownPlayers = await db.Players.Select(p => p.Id).ToListAsync(cancellationToken);

        foreach (var team in league.Teams)
        {
            if (!knownTeams.Contains(team.Id))
            {
                db.Teams.Add(new Team
                {
                    Id = team.Id,
                    Name = team.Name,
                    ShortName = team.ShortName,
                    Slug = team.Slug,
                });
            }

            db.TeamSeasons.Add(new TeamSeason { SeasonId = league.SeasonId, TeamId = team.Id });
            db.Standings.Add(new Standing { SeasonId = league.SeasonId, TeamId = team.Id });

            foreach (var player in team.Players.Where(p => !knownPlayers.Contains(p.Id)))
            {
                db.Players.Add(new Player
                {
                    Id = player.Id,
                    Name = player.Name,
                    TeamId = team.Id,
                    ShirtNumber = player.ShirtNumber,
                    Position = ToDomain(player.Position),
                });
            }
        }

        foreach (var fixture in league.Fixtures)
        {
            db.Matches.Add(new Match
            {
                Id = fixture.Id,
                SeasonId = league.SeasonId,
                Round = fixture.Round,
                KickoffUtc = fixture.KickoffUtc,
                HomeTeamId = fixture.Home.Id,
                AwayTeamId = fixture.Away.Id,
                Status = MatchStatus.Scheduled,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Position ToDomain(LineupPosition position) => position switch
    {
        LineupPosition.Goalkeeper => Position.Goalkeeper,
        LineupPosition.Defender => Position.Defender,
        LineupPosition.Midfielder => Position.Midfielder,
        _ => Position.Forward,
    };
}
