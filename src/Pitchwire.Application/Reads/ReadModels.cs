namespace Pitchwire.Application.Reads;

/// <summary>
/// A season, and the league it belongs to.
/// </summary>
/// <remarks>
/// A client has to start somewhere. Without this it would have to be told a season identifier out of
/// band, which is the sort of configuration that is wrong on somebody's machine for a week.
/// </remarks>
public sealed record SeasonSummary(Guid Id, int Year, Guid LeagueId, string League, string LeagueSlug);

/// <summary>
/// The stable choices within a season. Its rounds come from scheduled matches, not an assumption
/// about how many times its teams play each other.
/// </summary>
public sealed record SeasonDetail(Guid Id, int Year, LeagueRef League, IReadOnlyList<TeamRef> Teams, IReadOnlyList<int> Rounds);

/// <summary>
/// A team as every other read model refers to it.
/// </summary>
/// <remarks>
/// The slug travels with the identifier because a link in a browser reads better as the name of a club
/// than as a GUID, and the client should not have to keep its own table to build one.
/// </remarks>
public sealed record TeamRef(Guid Id, string Name, string ShortName, string Slug);

/// <summary>
/// The competition a match belongs to.
/// </summary>
/// <remarks>
/// Carried on every match summary because a live list spanning several countries is unreadable
/// without it: six matches in a row with no heading could be one league or six.
/// </remarks>
public sealed record LeagueRef(Guid Id, string Name, string Slug, string Country);

/// <summary>
/// A match as it appears in a list.
/// </summary>
/// <remarks>
/// <see cref="IsDegraded"/> is part of the contract rather than an internal detail. A score that is
/// known to be missing an event is still worth showing, and hiding the fact that it is incomplete
/// would be the dishonest half of that choice.
/// </remarks>
public sealed record MatchSummary(
    Guid Id,
    int Round,
    DateTimeOffset KickoffUtc,
    string Status,
    int Minute,
    int HomeScore,
    int AwayScore,
    bool IsDegraded,
    LeagueRef League,
    TeamRef Home,
    TeamRef Away);

public sealed record MatchEventView(
    int Sequence,
    int Minute,
    string Kind,
    Guid TeamId,
    string? Player,
    string? Assist,
    string? Replaced);

/// <summary>
/// A country and what leagues it has here.
/// </summary>
public sealed record CountrySummary(Guid Id, string Name, string Code, string Slug, int Leagues);

public sealed record LeagueSummary(Guid Id, string Name, string Slug, int Tier, Guid CountryId, string Country, Guid? CurrentSeasonId);

public sealed record MatchDetail(
    MatchSummary Match,
    IReadOnlyList<MatchEventView> Timeline,
    IReadOnlyList<TeamSheetView> Lineups,
    IReadOnlyList<TeamStatisticsView> Statistics);

public sealed record TableRow(
    int Position,
    TeamRef Team,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points);

public sealed record ScorerRow(Guid PlayerId, string Player, TeamRef Team, int Goals, int Assists);

/// <summary>
/// One of a team's recent results, from that team's point of view.
/// </summary>
public sealed record FormEntry(
    Guid MatchId,
    DateTimeOffset KickoffUtc,
    TeamRef Opponent,
    bool AtHome,
    int GoalsFor,
    int GoalsAgainst,
    string Outcome);
