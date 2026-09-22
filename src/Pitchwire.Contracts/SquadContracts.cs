namespace Pitchwire.Contracts;

/// <summary>
/// Where a provider says a player lined up.
/// </summary>
public enum LineupPosition
{
    Goalkeeper,
    Defender,
    Midfielder,
    Forward,
}

public sealed record LineupPlayerPayload(Guid PlayerId, int ShirtNumber, LineupPosition Position, bool Starter);

/// <summary>
/// One side's team sheet, as the provider publishes it before kick off.
/// </summary>
/// <remarks>
/// A snapshot rather than an event, so it has no sequence. The latest one for a match and a team
/// replaces whatever was there, which is also how a lost one heals: the provider sends it again at
/// half time and nothing has to be asked for.
/// </remarks>
public sealed record LineupPayload(Guid MatchId, Guid TeamId, string Formation, IReadOnlyList<LineupPlayerPayload> Players);

/// <summary>
/// One side's match statistics as of a given minute.
/// </summary>
/// <remarks>
/// Cumulative, so only the newest snapshot matters. One that arrives after a later one has already
/// been stored is older news and is ignored, which is the ordering rule for snapshots in the same way
/// the sequence number is the ordering rule for events.
/// </remarks>
public sealed record StatisticsPayload(
    Guid MatchId,
    Guid TeamId,
    int AsOfMinute,
    int Possession,
    int Shots,
    int ShotsOnTarget,
    int Corners,
    int Fouls,
    int Offsides);
