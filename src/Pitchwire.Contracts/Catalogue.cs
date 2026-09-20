using System.Security.Cryptography;
using System.Text;

namespace Pitchwire.Contracts;

public sealed record CataloguePlayer(Guid Id, string Name);

public sealed record CatalogueTeam(Guid Id, string Name, string ShortName, string Slug, IReadOnlyList<CataloguePlayer> Players);

public sealed record CatalogueFixture(Guid Id, int Round, DateTimeOffset KickoffUtc, CatalogueTeam Home, CatalogueTeam Away);

/// <summary>
/// The league both processes agree on: the same teams, the same squads, the same fixture list, with
/// the same identifiers on each side.
/// </summary>
/// <remarks>
/// A provider and its consumer have to agree on what a match is before they can disagree about the
/// score. Rather than ship a seed file and hope the two copies stay in step, every identifier here is
/// derived from a name, so both sides compute the same one without sharing a database or a file.
/// <para>
/// The teams are invented. Using real clubs would put somebody else's trademarks in a demo.
/// </para>
/// </remarks>
public static class Catalogue
{
    public const int SeasonYear = 2026;
    public const string LeagueName = "Pitchwire Premier";
    public const string LeagueCountry = "Testland";
    public const string LeagueSlug = "pitchwire-premier";

    private static readonly string[] TeamNames =
    [
        "Harbour Rovers", "Kingsway United", "Ironbridge City", "Northgate Athletic",
        "Saltmarsh Town", "Redhill Wanderers", "Castleford Albion", "Lakeside Rangers",
        "Oakvale County", "Stonebridge FC", "Westport Dynamo", "Fairmount Star",
    ];

    private static readonly string[] FirstNames =
    [
        "Arda", "Bruno", "Caleb", "Diego", "Emre", "Felix", "Goran", "Hugo",
        "Ivan", "Jonas", "Kerem", "Luca", "Mateo", "Nils", "Omar", "Pavel",
    ];

    private static readonly string[] Surnames =
    [
        "Aldridge", "Bakker", "Costa", "Duarte", "Eriksen", "Fontaine", "Grimaldi", "Halvorsen",
        "Iversen", "Jankovic", "Kovacs", "Lindqvist", "Moretti", "Navarro", "Okafor", "Pahlavi",
    ];

    private const int SquadSize = 14;

    public static Guid LeagueId { get; } = IdFor($"league:{LeagueSlug}");

    public static Guid SeasonId { get; } = IdFor($"season:{LeagueSlug}:{SeasonYear}");

    public static IReadOnlyList<CatalogueTeam> Teams { get; } = BuildTeams();

    public static IReadOnlyList<CatalogueFixture> Fixtures { get; } = BuildFixtures(Teams);

    /// <summary>
    /// A stable identifier for a name. Both processes derive the same value from the same string, so
    /// neither has to be told what the other decided.
    /// </summary>
    public static Guid IdFor(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        // SHA-256 rather than the usual UUID version 5 recipe, which specifies SHA-1. Nothing here is
        // a security decision, and SHA-1 would have to be justified to every analyzer that sees it.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(digest.AsSpan(0, 16));
    }

    private static List<CatalogueTeam> BuildTeams()
    {
        var teams = new List<CatalogueTeam>(TeamNames.Length);

        foreach (var name in TeamNames)
        {
            var slug = name.ToLowerInvariant().Replace(' ', '-');
            var players = new List<CataloguePlayer>(SquadSize);

            for (var shirt = 1; shirt <= SquadSize; shirt++)
            {
                // Derived from the club and the shirt number, so a squad is the same on both sides and
                // on every run without a list of 168 names in the repository.
                var first = FirstNames[(slug.Length + shirt) % FirstNames.Length];
                var last = Surnames[(slug.Length * shirt) % Surnames.Length];
                var playerName = $"{first} {last}";

                players.Add(new CataloguePlayer(IdFor($"player:{slug}:{shirt}"), playerName));
            }

            teams.Add(new CatalogueTeam(
                IdFor($"team:{slug}"),
                name,
                name[..3].ToUpperInvariant(),
                slug,
                players));
        }

        return teams;
    }

    private static List<CatalogueFixture> BuildFixtures(IReadOnlyList<CatalogueTeam> teams)
    {
        // The circle method: one team stays put and the rest rotate around it, which gives every pair
        // exactly one meeting across the season.
        var rotation = teams.ToList();
        var seasonStart = new DateTimeOffset(SeasonYear, 8, 15, 16, 0, 0, TimeSpan.Zero);
        var fixtures = new List<CatalogueFixture>();

        for (var round = 1; round < teams.Count; round++)
        {
            for (var pair = 0; pair < teams.Count / 2; pair++)
            {
                var home = rotation[pair];
                var away = rotation[^(pair + 1)];

                // Alternated so no side plays every match at home.
                if (round % 2 == 0)
                {
                    (home, away) = (away, home);
                }

                var kickoff = seasonStart
                    .AddDays(7 * (round - 1))
                    .AddHours(pair % 3 * 2);

                fixtures.Add(new CatalogueFixture(
                    IdFor($"match:{SeasonYear}:{round}:{home.Slug}:{away.Slug}"),
                    round,
                    kickoff,
                    home,
                    away));
            }

            var last = rotation[^1];
            rotation.RemoveAt(rotation.Count - 1);
            rotation.Insert(1, last);
        }

        return fixtures;
    }
}
