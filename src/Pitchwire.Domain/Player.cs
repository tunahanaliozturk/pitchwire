namespace Pitchwire.Domain;

public sealed class Player
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    /// <summary>
    /// The team the player currently belongs to. Squad membership per season arrives with transfers,
    /// which this service does not model; until then a single current team is what a team sheet needs.
    /// </summary>
    public Guid TeamId { get; set; }

    public int ShirtNumber { get; set; }

    /// <summary>Where the player usually lines up. A team sheet can say otherwise for one match.</summary>
    public Position Position { get; set; }

    public Team? Team { get; set; }
}
