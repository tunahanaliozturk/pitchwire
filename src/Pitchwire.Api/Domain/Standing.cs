namespace Pitchwire.Api.Domain;

/// <summary>
/// A team's league table row for one season, maintained as matches finish.
/// </summary>
/// <remarks>
/// Kept rather than computed on request because a table query that scans every match of a season is
/// the slowest page on a site like this, and because the rebuild that would replace it is exactly what
/// the recompute command does when the two need to be compared.
/// </remarks>
public sealed class Standing
{
    public Guid SeasonId { get; set; }

    public Guid TeamId { get; set; }

    public int Played { get; set; }

    public int Won { get; set; }

    public int Drawn { get; set; }

    public int Lost { get; set; }

    public int GoalsFor { get; set; }

    public int GoalsAgainst { get; set; }

    public int Points { get; set; }

    /// <summary>
    /// Derived in memory rather than stored. Two columns that must agree are two columns that can
    /// disagree, and the subtraction costs nothing.
    /// </summary>
    public int GoalDifference => GoalsFor - GoalsAgainst;

    public Team? Team { get; set; }
}
