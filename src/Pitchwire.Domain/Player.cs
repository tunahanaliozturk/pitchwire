namespace Pitchwire.Domain;

public sealed class Player
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    /// <summary>
    /// The team the player currently belongs to. Squad membership per season arrives with lineups;
    /// until then a single current team is all the event stream needs to name a scorer.
    /// </summary>
    public Guid TeamId { get; set; }

    public Team? Team { get; set; }
}
