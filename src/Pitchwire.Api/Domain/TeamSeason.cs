namespace Pitchwire.Api.Domain;

/// <summary>
/// A team's participation in one season. The pair is the key, so the same team can appear in several
/// seasons and in different leagues across them.
/// </summary>
public sealed class TeamSeason
{
    public Guid SeasonId { get; set; }

    public Guid TeamId { get; set; }

    public Season? Season { get; set; }

    public Team? Team { get; set; }
}
