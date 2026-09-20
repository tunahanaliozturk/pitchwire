namespace Pitchwire.Api.Domain;

/// <summary>
/// One league's run of one season. The league lives here rather than on the team, so a promoted team
/// simply appears in a different league's season and its earlier fixtures stay correct.
/// </summary>
public sealed class Season
{
    public Guid Id { get; init; }

    public Guid LeagueId { get; set; }

    /// <summary>The starting year. 2026 is the 2026/27 season.</summary>
    public int Year { get; set; }

    public League? League { get; set; }
}
