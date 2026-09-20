namespace Pitchwire.Api.Domain;

public sealed class Team
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    /// <summary>What fits in a live score row on a phone.</summary>
    public required string ShortName { get; set; }

    public required string Slug { get; set; }
}
