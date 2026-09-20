namespace Pitchwire.Feed;

/// <summary>
/// How the simulated provider behaves, including how badly.
/// </summary>
/// <remarks>
/// The misbehaviour is on by default rather than reserved for tests. A provider that only duplicates
/// events in a test suite lets the running application hide the bug, and the whole point of this
/// process is to make the ingestion boundary face what a real feed does.
/// </remarks>
public sealed class FeedOptions
{
    public const string SectionName = "Feed";

    /// <summary>Shared with the API. Every batch is signed with it.</summary>
    public string Secret { get; set; } = string.Empty;

    public Uri ApiBaseAddress { get; set; } = new("http://localhost:5080/");

    /// <summary>The same seed replays the same season, which is what makes a bug reproducible.</summary>
    public ulong Seed { get; set; } = 1337;

    /// <summary>
    /// Match seconds per real second. At 60 a ninety minute match takes ninety seconds, which is long
    /// enough to watch and short enough that a demo does not need an afternoon.
    /// </summary>
    public double ClockFactor { get; set; } = 60;

    /// <summary>How often an event is sent a second time.</summary>
    public double DuplicateRate { get; set; } = 0.05;

    /// <summary>How often an event is held back and sent after the one behind it.</summary>
    public double ReorderRate { get; set; } = 0.05;

    /// <summary>How often an event is never sent at all, and has to be asked for.</summary>
    public double DropRate { get; set; } = 0.02;

    /// <summary>How often the provider goes quiet and then sends a burst.</summary>
    public double BurstPauseRate { get; set; } = 0.05;

    /// <summary>How long a burst pause lasts, in match minutes.</summary>
    public int BurstPauseMinutes { get; set; } = 3;

    /// <summary>How long to wait between rounds of the season, in real seconds.</summary>
    public int RoundIntervalSeconds { get; set; } = 30;
}
