using Microsoft.EntityFrameworkCore;
using Pitchwire.Contracts;
using Pitchwire.Domain;
using Pitchwire.Infrastructure.Persistence;

namespace Pitchwire.TestSupport;

/// <summary>
/// The smallest world a match can exist in: one league, one season, two teams.
/// </summary>
public static class Seed
{
    public static async Task<Match> MatchAsync(PitchwireDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        // One test country per database, found or made. ZZ is a code ISO leaves for private use, so it
        // can never collide with a real one the catalogue seeds.
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Code == "ZZ", cancellationToken);

        if (country is null)
        {
            country = new Country { Id = Guid.NewGuid(), Name = "Testland", Code = "ZZ", Slug = "testland" };
            db.Countries.Add(country);
        }

        var league = new League { Id = Guid.NewGuid(), Name = "Test League", CountryId = country.Id, Slug = $"test-league-{Guid.NewGuid():N}" };
        var season = new Season { Id = Guid.NewGuid(), LeagueId = league.Id, Year = 2026 };
        var home = new Team { Id = Guid.NewGuid(), Name = "Home United", ShortName = "HOM", Slug = $"home-{Guid.NewGuid():N}" };
        var away = new Team { Id = Guid.NewGuid(), Name = "Away City", ShortName = "AWY", Slug = $"away-{Guid.NewGuid():N}" };

        var match = new Match
        {
            Id = Guid.NewGuid(),
            SeasonId = season.Id,
            Round = 1,
            KickoffUtc = new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero),
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            Status = MatchStatus.Scheduled,
        };

        db.Leagues.Add(league);
        db.Seasons.Add(season);
        db.Teams.AddRange(home, away);
        db.TeamSeasons.AddRange(
            new TeamSeason { SeasonId = season.Id, TeamId = home.Id },
            new TeamSeason { SeasonId = season.Id, TeamId = away.Id });
        db.Matches.Add(match);

        await db.SaveChangesAsync(cancellationToken);

        return match;
    }

    public static MatchEvent Event(
        Match match,
        int sequence,
        string providerEventId,
        string provider = "simulator",
        Domain.MatchEventKind kind = Domain.MatchEventKind.Goal,
        int minute = 10)
    {
        ArgumentNullException.ThrowIfNull(match);

        return new MatchEvent
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Provider = provider,
            ProviderEventId = providerEventId,
            Sequence = sequence,
            Minute = minute,
            Kind = kind,
            TeamId = match.HomeTeamId,
            OccurredAt = match.KickoffUtc.AddMinutes(minute),
            ReceivedAt = match.KickoffUtc.AddMinutes(minute),
        };
    }
}
