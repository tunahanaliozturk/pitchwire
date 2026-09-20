namespace Pitchwire.Application.Reads;

/// <summary>
/// A team as every other read model refers to it.
/// </summary>
/// <remarks>
/// The slug travels with the identifier because a link in a browser reads better as the name of a club
/// than as a GUID, and the client should not have to keep its own table to build one.
/// </remarks>
public sealed record TeamRef(Guid Id, string Name, string ShortName, string Slug);

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
    TeamRef Home,
    TeamRef Away);

public sealed record MatchEventView(
    int Sequence,
    int Minute,
    string Kind,
    Guid TeamId,
    string? Player,
    string? Assist);

public sealed record MatchDetail(MatchSummary Match, IReadOnlyList<MatchEventView> Timeline);

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
