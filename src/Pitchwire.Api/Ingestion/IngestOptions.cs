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
    /// The signature covers the whole body, so the body has to be buffered before it can be checked.
    /// This is the cap on what will be buffered.
    /// </summary>
    public int MaxBodyBytes { get; set; } = 1024 * 1024;
}
