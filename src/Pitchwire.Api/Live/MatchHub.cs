using Microsoft.AspNetCore.SignalR;

namespace Pitchwire.Api.Live;

/// <summary>
/// What a browser subscribes to while it is watching.
/// </summary>
/// <remarks>
/// Three kinds of group, because three screens ask different questions. A match detail wants
/// everything about one match. A favourite wants anything about one team. The live list wants a
/// little about every match at once, and giving it one group rather than a membership per match is
/// what keeps a Saturday evening from costing two hundred joins per client.
/// </remarks>
public sealed class MatchHub : Hub
{
    public const string LiveGroup = "live";

    public static string MatchGroup(Guid matchId) => $"match:{matchId}";

    public static string TeamGroup(Guid teamId) => $"team:{teamId}";

    public Task WatchLive() => Groups.AddToGroupAsync(Context.ConnectionId, LiveGroup);

    public Task StopWatchingLive() => Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroup);

    public Task WatchMatch(Guid matchId) => Groups.AddToGroupAsync(Context.ConnectionId, MatchGroup(matchId));

    public Task StopWatchingMatch(Guid matchId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, MatchGroup(matchId));

    public Task WatchTeam(Guid teamId) => Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));

    public Task StopWatchingTeam(Guid teamId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, TeamGroup(teamId));
}
