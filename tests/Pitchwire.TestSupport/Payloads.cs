using Pitchwire.Api.Domain;
using Pitchwire.Contracts;

namespace Pitchwire.TestSupport;

/// <summary>
/// Events as the provider would send them over the wire.
/// </summary>
public static class Payloads
{
    public static MatchEventPayload Goal(Match match, int sequence, int minute, bool forHome) =>
        Event(match, sequence, minute, MatchEventKind.Goal, forHome);

    public static MatchEventPayload Event(
        Match match,
        int sequence,
        int minute,
        MatchEventKind kind,
        bool forHome,
        string? providerEventId = null)
    {
        ArgumentNullException.ThrowIfNull(match);

        return new MatchEventPayload(
            ProviderEventId: providerEventId ?? $"{match.Id:N}-{sequence}",
            MatchId: match.Id,
            Sequence: sequence,
            Minute: minute,
            Kind: kind,
            TeamId: forHome ? match.HomeTeamId : match.AwayTeamId,
            PlayerId: null,
            AssistPlayerId: null,
            OccurredAt: match.KickoffUtc.AddMinutes(minute));
    }
}
