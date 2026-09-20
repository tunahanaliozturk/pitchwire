namespace Pitchwire.Domain;

public enum MatchStatus
{
    Scheduled,
    Live,
    Halftime,
    Finished,
    Postponed,
}

/// <summary>
/// A fixture and the state derived from its events.
/// </summary>
/// <remarks>
/// The score, minute and status are derived data. The event log is what is true, and these columns
/// exist because a live list that replayed every event on every request would not be a live list.
/// <see cref="LastEventSequence"/> is what makes a late or missing delivery detectable at all: without
/// it, an event that arrives out of order is indistinguishable from the next one.
/// </remarks>
public sealed class Match
{
    public Guid Id { get; init; }

    public Guid SeasonId { get; set; }

    public int Round { get; set; }

    public DateTimeOffset KickoffUtc { get; set; }

    public Guid HomeTeamId { get; set; }

    public Guid AwayTeamId { get; set; }

    public MatchStatus Status { get; set; }

    public int HomeScore { get; set; }

    public int AwayScore { get; set; }

    public int Minute { get; set; }

    public int LastEventSequence { get; set; }

    /// <summary>
    /// When an event for this match last arrived. A live match that has gone quiet for too long is
    /// how a dropped final whistle shows itself, and nothing else in the row would reveal it.
    /// </summary>
    public DateTimeOffset? LastEventAt { get; set; }

    /// <summary>
    /// Set when the feed skipped a sequence number. The match is still served, and it says so, because
    /// showing a score that is known to be incomplete without saying so is the worse of the two.
    /// </summary>
    public bool IsDegraded { get; set; }

    public Season? Season { get; set; }

    public Team? HomeTeam { get; set; }

    public Team? AwayTeam { get; set; }
}
