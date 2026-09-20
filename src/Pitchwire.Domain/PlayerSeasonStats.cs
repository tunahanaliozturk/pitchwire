namespace Pitchwire.Domain;

/// <summary>
/// What a player did across one season, maintained as events arrive. The top scorer list reads this
/// rather than counting goal events on every request.
/// </summary>
public sealed class PlayerSeasonStats
{
    public Guid SeasonId { get; set; }

    public Guid PlayerId { get; set; }

    public int Goals { get; set; }

    public int Assists { get; set; }

    public int YellowCards { get; set; }

    public int RedCards { get; set; }

    public Player? Player { get; set; }
}
