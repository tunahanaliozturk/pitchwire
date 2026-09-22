using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pitchwire.Contracts;

public sealed record CatalogueCountry(Guid Id, string Name, string Code, string Slug);

public sealed record CataloguePlayer(Guid Id, string Name, int ShirtNumber, LineupPosition Position);

public sealed record CatalogueTeam(Guid Id, string Name, string ShortName, string Slug, IReadOnlyList<CataloguePlayer> Players);

public sealed record CatalogueFixture(Guid Id, Guid SeasonId, int Round, DateTimeOffset KickoffUtc, CatalogueTeam Home, CatalogueTeam Away);

public sealed record CatalogueLeague(
    Guid Id,
    string Name,
    string Slug,
    int Tier,
    CatalogueCountry Country,
    Guid SeasonId,
    IReadOnlyList<CatalogueTeam> Teams,
    IReadOnlyList<CatalogueFixture> Fixtures);

/// <summary>
/// The world both processes agree on: countries, their leagues, the squads and the fixture lists,
/// with the same identifiers on each side.
/// </summary>
/// <remarks>
/// A provider and its consumer have to agree on what a match is before they can disagree about the
/// score. Rather than ship a seed file and hope the two copies stay in step, every identifier is
/// derived from a name, so both sides compute the same one without sharing a database or a file.
/// <para>
/// The countries are real and every club, league and player is invented. Real clubs would put
/// somebody else's trademarks in a demo; invented ones in a real country let a reader in Istanbul see
/// names that look like home without anybody's badge on them.
/// </para>
/// <para>
/// The first league is the one the tests and the demo default to. Its identifiers are exactly the ones
/// the single league version produced, so nothing stored before the change moves.
/// </para>
/// </remarks>
public static class Catalogue
{
    public const int SeasonYear = 2026;
    public const string LeagueName = "Pitchwire Premier";
    public const string LeagueSlug = "pitchwire-premier";

    private const int SquadSize = 18;

    private static readonly DateTimeOffset SeasonStart = new(SeasonYear, 8, 15, 16, 0, 0, TimeSpan.Zero);

    /// <summary>Words that start a club's name without telling two clubs apart.</summary>
    private static readonly HashSet<string> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Real", "Atlético", "CD", "CF", "UD", "FC", "FK", "SK", "Club", "Unión", "Deportivo", "Racing", "Sporting",
    };

    private static readonly (string Name, string Code, string[] First, string[] Last, (string Name, string Slug, int Tier, string[] Teams)[] Leagues)[] World =
    [
        ("England", "GB",
            ["Arda", "Bruno", "Caleb", "Diego", "Emre", "Felix", "Goran", "Hugo", "Ivan", "Jonas", "Kerem", "Luca", "Mateo", "Nils", "Omar", "Pavel"],
            ["Aldridge", "Bakker", "Costa", "Duarte", "Eriksen", "Fontaine", "Grimaldi", "Halvorsen", "Iversen", "Jankovic", "Kovacs", "Lindqvist", "Moretti", "Navarro", "Okafor", "Pahlavi"],
            [
                (LeagueName, LeagueSlug, 1,
                [
                    "Harbour Rovers", "Kingsway United", "Ironbridge City", "Northgate Athletic",
                    "Saltmarsh Town", "Redhill Wanderers", "Castleford Albion", "Lakeside Rangers",
                    "Oakvale County", "Stonebridge FC", "Westport Dynamo", "Fairmount Star",
                ]),
                ("Northern Championship", "northern-championship", 2,
                [
                    "Millbrook Town", "Ashford Borough", "Whitcombe Athletic", "Pennmoor United", "Carrow Vale",
                    "Blackwater Rovers", "Hollins End", "Fenwick City", "Greystone Albion", "Marlow Heath",
                ]),
            ]),
        ("Türkiye", "TR",
            ["Emre", "Burak", "Kerem", "Arda", "Mert", "Can", "Yusuf", "Ozan", "Barış", "Onur", "Serkan", "Hakan", "Tolga", "Umut", "Deniz", "Eren"],
            ["Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Aydın", "Yıldız", "Arslan", "Doğan", "Kılıç", "Aslan", "Koç", "Kurt", "Özdemir", "Polat", "Erdem"],
            [
                ("Anatolia League", "anatolia-league", 1,
                [
                    "Boğaziçi Spor", "Kapadokya FK", "Ege Yıldızı", "Karadeniz Gücü", "Toros Spor",
                    "Marmara Birlik", "Anadolu Kartal", "Fırat Gençlik", "Uludağ Spor", "Akdeniz Dalga",
                ]),
                ("Anatolia First", "anatolia-first", 2,
                [
                    "Kızılırmak FK", "Göksu Spor", "Ilgaz Birlik", "Menderes Yıldız", "Sakarya Vadisi",
                    "Nemrut Spor", "Bozdağ FK", "Munzur Gücü", "Palandöken SK", "Kaçkar Spor",
                ]),
            ]),
        ("Spain", "ES",
            ["Álvaro", "Diego", "Javier", "Pablo", "Sergio", "Iker", "Hugo", "Marcos", "Adrián", "Rubén", "Unai", "Óscar", "Raúl", "Mario", "Iván", "Jorge"],
            ["García", "Martínez", "López", "Sánchez", "Pérez", "Gómez", "Ruiz", "Díaz", "Moreno", "Álvarez", "Romero", "Navarro", "Torres", "Domínguez", "Vázquez", "Ramos"],
            [
                ("Iberia Primera", "iberia-primera", 1,
                [
                    "Real Alcázar", "Atlético Serrano", "Unión Mareña", "Deportivo Castell", "CD Valdemar",
                    "Racing Montaña", "Sporting Olivar", "CF Marisma", "Club Sierra Alta", "Real Tajo",
                ]),
                ("Iberia Segunda", "iberia-segunda", 2,
                [
                    "UD Almenara", "CD Robledal", "Atlético Vega", "Real Brezo", "Unión Ribera",
                    "CF Encinar", "Deportivo Cumbre", "Racing Salinas", "Club Albufera", "Sporting Páramo",
                ]),
            ]),
    ];

    public static IReadOnlyList<CatalogueCountry> Countries { get; }

    public static IReadOnlyList<CatalogueLeague> Leagues { get; }

    public static IReadOnlyList<CatalogueFixture> AllFixtures { get; }

    public static IReadOnlyList<CatalogueTeam> AllTeams { get; }

    /// <summary>The league the demo and the tests start from.</summary>
    public static CatalogueLeague Primary => Leagues[0];

    public static Guid LeagueId => Primary.Id;

    public static Guid SeasonId => Primary.SeasonId;

    public static IReadOnlyList<CatalogueTeam> Teams => Primary.Teams;

    public static IReadOnlyList<CatalogueFixture> Fixtures => Primary.Fixtures;

#pragma warning disable CA1810 // One static constructor is clearer than a chain of initialisers that depend on each other.
    static Catalogue()
#pragma warning restore CA1810
    {
        var countries = new List<CatalogueCountry>();
        var leagues = new List<CatalogueLeague>();
        var leagueOffset = 0;

        foreach (var (countryName, code, first, last, leagueSpecs) in World)
        {
            var country = new CatalogueCountry(IdFor($"country:{code}"), countryName, code, Slug(countryName));
            countries.Add(country);

            foreach (var (name, slug, tier, teamNames) in leagueSpecs)
            {
                var seasonId = IdFor($"season:{slug}:{SeasonYear}");
                var teams = teamNames.Select(teamName => BuildTeam(teamName, first, last)).ToList();

                leagues.Add(new CatalogueLeague(
                    IdFor($"league:{slug}"),
                    name,
                    slug,
                    tier,
                    country,
                    seasonId,
                    teams,
                    BuildFixtures(seasonId, teams, TimeSpan.FromMinutes(15 * leagueOffset))));

                leagueOffset++;
            }
        }

        Countries = countries;
        Leagues = leagues;
        AllFixtures = [.. leagues.SelectMany(league => league.Fixtures)];
        AllTeams = [.. leagues.SelectMany(league => league.Teams)];
    }

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

    /// <summary>
    /// A readable identifier for a URL. "Boğaziçi Spor" becomes "bogazici-spor", so an address can be
    /// typed on any keyboard and still reads as the club.
    /// </summary>
    public static string Slug(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Dotless i first: it has no decomposition, so the accent stripping below would leave it.
        var normalised = name
            .Replace('ı', 'i')
            .Replace('İ', 'I')
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(normalised.Length);

        foreach (var character in normalised)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        return string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static CatalogueTeam BuildTeam(string name, string[] firstNames, string[] surnames)
    {
        var slug = Slug(name);
        var players = new List<CataloguePlayer>(SquadSize);

        for (var shirt = 1; shirt <= SquadSize; shirt++)
        {
            var key = IdFor($"player:{slug}:{shirt}");

            // Picked from the bytes of the player's own identifier, which both processes compute the
            // same way. string.GetHashCode would not do: it is randomised per process, and the provider
            // and the consumer would disagree about who scored.
            var bytes = key.ToByteArray();
            var playerName = $"{firstNames[bytes[0] % firstNames.Length]} {surnames[bytes[1] % surnames.Length]}";

            players.Add(new CataloguePlayer(key, playerName, shirt, PositionFor(shirt)));
        }

        return new CatalogueTeam(IdFor($"team:{slug}"), name, ShortName(name), slug, players);
    }

    /// <summary>
    /// Two keepers, six defenders, six midfielders and four forwards, which is enough to name a match
    /// day squad and a bench in any of the formations the simulator plays.
    /// </summary>
    private static LineupPosition PositionFor(int shirt) => shirt switch
    {
        1 or 13 => LineupPosition.Goalkeeper,
        2 or 3 or 4 or 5 or 12 or 14 => LineupPosition.Defender,
        6 or 7 or 8 or 10 or 15 or 16 => LineupPosition.Midfielder,
        _ => LineupPosition.Forward,
    };

    /// <summary>
    /// Three letters from the word that tells a club apart. "Real Alcázar" and "Real Tajo" would both
    /// be REA otherwise, and a live list of three REA rows is a list nobody can read.
    /// </summary>
    private static string ShortName(string name)
    {
        var distinctive = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(word => !Prefixes.Contains(word)) ?? name;

        return distinctive[..Math.Min(3, distinctive.Length)].ToUpper(CultureInfo.InvariantCulture);
    }

    private static List<CatalogueFixture> BuildFixtures(Guid seasonId, List<CatalogueTeam> teams, TimeSpan offset)
    {
        // The circle method: one team stays put and the rest rotate around it, which gives every pair
        // exactly one meeting across the season.
        var rotation = teams.ToList();
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

                var kickoff = SeasonStart
                    .AddDays(7 * (round - 1))
                    .AddHours(pair % 3 * 2)
                    .Add(offset);

                fixtures.Add(new CatalogueFixture(
                    IdFor($"match:{SeasonYear}:{round}:{home.Slug}:{away.Slug}"),
                    seasonId,
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
