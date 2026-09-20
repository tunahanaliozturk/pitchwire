using Microsoft.AspNetCore.SignalR;
using Pitchwire.Application.Live;

namespace Pitchwire.Api.Live;

/// <summary>
/// Sends a match update to everyone who asked for it.
/// </summary>
/// <remarks>
/// One send per group rather than one per connection, and the groups are chosen so a connection
/// watching a match through two of them still gets the update once: SignalR delivers a message to a
/// connection a single time even when it belongs to several of the groups addressed together.
/// </remarks>
internal sealed class SignalRLiveUpdates(IHubContext<MatchHub> hub) : ILiveUpdates
{
    public const string UpdateMethod = "matchUpdated";

    public Task PublishAsync(MatchUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        return hub.Clients
            .Groups(
                MatchHub.LiveGroup,
                MatchHub.MatchGroup(update.MatchId),
                MatchHub.TeamGroup(update.HomeTeamId),
                MatchHub.TeamGroup(update.AwayTeamId))
            .SendAsync(UpdateMethod, update, cancellationToken);
    }
}
