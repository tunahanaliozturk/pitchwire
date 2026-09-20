namespace Pitchwire.Domain;

public sealed class League
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    public required string Country { get; set; }

    /// <summary>Stable, readable identifier for URLs. Names change spelling, slugs do not.</summary>
    public required string Slug { get; set; }
}
