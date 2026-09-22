namespace Pitchwire.Domain;

public sealed class Country
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    /// <summary>ISO 3166 alpha-2, which is what a flag is looked up by.</summary>
    public required string Code { get; set; }

    public required string Slug { get; set; }
}

public sealed class League
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    public Guid CountryId { get; set; }

    /// <summary>
    /// One for the top flight, two for the one below it. What a country's league list is ordered by,
    /// because alphabetical order would put the second division first half the time.
    /// </summary>
    public int Tier { get; set; } = 1;

    /// <summary>Stable, readable identifier for URLs. Names change spelling, slugs do not.</summary>
    public required string Slug { get; set; }

    public Country? Country { get; set; }
}
