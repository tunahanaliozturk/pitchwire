namespace Pitchwire.Api.Ingestion;

/// <summary>
/// How the ingestion boundary treats the provider it listens to.
/// </summary>
public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    /// <summary>
    /// Shared with the provider and never logged. Supplied by user secrets in development and by the
    /// environment in a container.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// The name events are stored under. It is half of the uniqueness key, so changing it for a
    /// running provider would make every redelivery look new.
    /// </summary>
    public string Provider { get; set; } = "simulator";

    /// <summary>
    /// How far a request timestamp may sit from now, in either direction. Wide enough for ordinary
    /// clock skew, narrow enough that a captured batch cannot be held and replayed later.
    /// </summary>
    public TimeSpan ReplayTolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Where the provider answers questions about what it already sent. A push feed cannot be asked
    /// for what it forgot to push, so this address is what makes a gap recoverable.
    /// </summary>
    public Uri FeedBaseAddress { get; set; } = new("http://localhost:5081/");

    /// <summary>
    /// How long a live match may go without an event before the service asks the provider what it
    /// missed. A dropped final whistle leaves a match live forever otherwise, because there is no
    /// later event to reveal the hole.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often quiet matches are looked for.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The signature covers the whole body, so the body has to be buffered before it can be checked.
    /// This is the cap on what will be buffered.
    /// </summary>
    public int MaxBodyBytes { get; set; } = 1024 * 1024;
}
