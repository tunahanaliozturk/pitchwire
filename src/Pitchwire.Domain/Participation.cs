namespace Pitchwire.Domain;

/// <summary>
/// How long each player was on the pitch.
/// </summary>
/// <remarks>
/// Worked out from the team sheet and the events rather than reported, because the event log already
/// says everything it depends on: who started, who came off and when, and who was sent off. A number
/// the provider sent separately could disagree with the log, and then one of the two would be wrong.
/// </remarks>
public static class Participation
{
    public static IReadOnlyDictionary<Guid, int> MinutesPlayed(
        IEnumerable<MatchLineupEntry> lineup,
        IEnumerable<MatchEvent> events,
        int finalMinute)
    {
        ArgumentNullException.ThrowIfNull(lineup);
        ArgumentNullException.ThrowIfNull(events);

        var cameOn = new Dictionary<Guid, int>();
        var wentOff = new Dictionary<Guid, int>();

        foreach (var starter in lineup.Where(entry => entry.IsStarter))
        {
            cameOn[starter.PlayerId] = 0;
        }

        foreach (var @event in events.OrderBy(e => e.Sequence))
        {
            switch (@event.Kind)
            {
                case MatchEventKind.Substitution:
                    if (@event.ReplacedPlayerId is { } replaced && cameOn.ContainsKey(replaced))
                    {
                        wentOff.TryAdd(replaced, @event.Minute);
                    }

                    if (@event.PlayerId is { } substitute)
                    {
                        cameOn.TryAdd(substitute, @event.Minute);
                    }

                    break;

                case MatchEventKind.Red:
                    // Sent off is off, whatever the provider does with the lineup afterwards.
                    if (@event.PlayerId is { } dismissed && cameOn.ContainsKey(dismissed))
                    {
                        wentOff.TryAdd(dismissed, @event.Minute);
                    }

                    break;

                default:
                    break;
            }
        }

        return cameOn.ToDictionary(
            pair => pair.Key,
            pair => Math.Max(0, (wentOff.TryGetValue(pair.Key, out var off) ? off : finalMinute) - pair.Value));
    }
}
