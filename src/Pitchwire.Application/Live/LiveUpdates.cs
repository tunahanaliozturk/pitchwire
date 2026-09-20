namespace Pitchwire.Application.Live;

/// <summary>
/// What changed about a match, small enough to send on every event.
/// </summary>
/// <remarks>
/// A delta rather than the match document. A busy evening is a few hundred events a minute across
/// every watcher, and sending the whole match each time would spend most of the bandwidth resending
/// two team names that have not changed since kick off.
/// <para>
/// <see cref="Event"/> is absent when the update is a correction. A late delivery rebuilds the score
/// from the log, and there is no single event to append: the client that cares about the timeline
/// asks for it again, and the client showing only the score already has what it needs.
/// </para>
/// </remarks>
/// <summary>
/// The event behind an update, carried by identifier rather than by name.
/// </summary>
/// <remarks>
/// Names would mean a lookup per event on the way out, and a client showing a scorer already holds
/// the squad or can ask for the timeline. Identifiers keep the delta small and the publish path free
/// of a query.
/// </remarks>
public sealed record MatchUpdateEvent(
    int Sequence,
    int Minute,
    string Kind,
    Guid TeamId,
    Guid? PlayerId,
    Guid? AssistPlayerId);

public sealed record MatchUpdate(
    Guid MatchId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    int Sequence,
    int Minute,
    string Status,
    int HomeScore,
    int AwayScore,
    bool IsDegraded,
    MatchUpdateEvent? Event);

/// <summary>
/// Where a match update goes once the transaction that produced it has committed.
/// </summary>
/// <remarks>
/// A port, because the application layer decides what changed and the host decides how that reaches a
/// browser. It also has a second implementation that records rather than sends, which is how the
/// ingestion tests check what would have been published without a socket in the way.
/// </remarks>
public interface ILiveUpdates
{
    Task PublishAsync(MatchUpdate update, CancellationToken cancellationToken);
}
