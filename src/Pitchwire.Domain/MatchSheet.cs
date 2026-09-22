namespace Pitchwire.Domain;

/// <summary>
/// How one side lined up for one match.
/// </summary>
public sealed class MatchTeamSheet
{
    public Guid MatchId { get; set; }

    public Guid TeamId { get; set; }

    /// <summary>As the provider writes it, such as 4-3-3. Shown, never computed with.</summary>
    public required string Formation { get; set; }
}

/// <summary>
/// One player named on a team sheet.
/// </summary>
public sealed class MatchLineupEntry
{
    public Guid MatchId { get; set; }

    public Guid TeamId { get; set; }

    public Guid PlayerId { get; set; }

    public int ShirtNumber { get; set; }

    public Position Position { get; set; }

    /// <summary>In the starting eleven, as opposed to named on the bench.</summary>
    public bool IsStarter { get; set; }

    public Player? Player { get; set; }
}

/// <summary>
/// One side's match statistics as of a minute.
/// </summary>
/// <remarks>
/// Only the newest snapshot is kept. The numbers are cumulative, so an older one carries nothing the
/// newer one does not, and keeping every snapshot would store the same match eight times over.
/// </remarks>
public sealed class MatchStatistics
{
    public Guid MatchId { get; set; }

    public Guid TeamId { get; set; }

    public int AsOfMinute { get; set; }

    /// <summary>A percentage. The two sides add up to a hundred.</summary>
    public int Possession { get; set; }

    public int Shots { get; set; }

    public int ShotsOnTarget { get; set; }

    public int Corners { get; set; }

    public int Fouls { get; set; }

    public int Offsides { get; set; }
}
