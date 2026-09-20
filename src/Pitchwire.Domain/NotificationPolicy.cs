namespace Pitchwire.Domain;

/// <summary>
/// The kinds of moment a device can ask to hear about.
/// </summary>
/// <remarks>
/// Coarser than the event log on purpose. Nobody wants a separate switch for a penalty and an open
/// play goal, and a preference screen with eight toggles is a preference screen nobody finishes.
/// </remarks>
public enum NotificationTrigger
{
    Goal,
    RedCard,
    Kickoff,
    FullTime,
}

/// <summary>
/// Who hears about what, and when they have asked not to.
/// </summary>
public static class NotificationPolicy
{
    /// <summary>
    /// What, if anything, this event is worth telling somebody about.
    /// </summary>
    /// <remarks>
    /// Kick off is the first event of a match rather than any period starting, or the restart after
    /// half time would announce a second kick off that nobody asked for. Full time is a period ending
    /// that left the match finished, for the same reason in reverse.
    /// </remarks>
    public static NotificationTrigger? TriggerFor(MatchEventKind kind, int sequence, MatchStatus statusAfter) => kind switch
    {
        MatchEventKind.Goal or MatchEventKind.OwnGoal or MatchEventKind.PenaltyGoal => NotificationTrigger.Goal,
        MatchEventKind.Red => NotificationTrigger.RedCard,
        MatchEventKind.PeriodStart when sequence == 1 => NotificationTrigger.Kickoff,
        MatchEventKind.PeriodEnd when statusAfter == MatchStatus.Finished => NotificationTrigger.FullTime,
        _ => null,
    };

    public static bool Wants(Device device, NotificationTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(device);

        return trigger switch
        {
            NotificationTrigger.Goal => device.NotifyOnGoal,
            NotificationTrigger.RedCard => device.NotifyOnRedCard,
            NotificationTrigger.Kickoff => device.NotifyOnKickoff,
            NotificationTrigger.FullTime => device.NotifyOnFullTime,
            _ => false,
        };
    }

    /// <summary>
    /// Whether this device should be told, given that it follows a team in the match.
    /// </summary>
    /// <remarks>
    /// Three conditions, in the order they are cheapest to answer. The quiet hours check is last
    /// because it needs the device's zone, and there is no point resolving a zone for a device that
    /// did not want the event in the first place.
    /// </remarks>
    public static bool ShouldNotify(Device device, NotificationTrigger trigger, TimeZoneInfo zone, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(device);

        return Wants(device, trigger)
            && !QuietHours.IsQuiet(device.QuietHoursStart, device.QuietHoursEnd, zone, now);
    }
}

/// <summary>
/// A notification that has been decided on but not yet delivered.
/// </summary>
/// <remarks>
/// Written in the same transaction as the event that caused it. Sending from inside that transaction
/// would notify people about goals a rollback then removed, and sending after it without a record
/// would lose every notification the process was holding when it died.
/// </remarks>
public sealed class NotificationOutboxEntry
{
    public Guid Id { get; init; }

    public Guid DeviceId { get; set; }

    public Guid MatchEventId { get; set; }

    public Guid MatchId { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Set when the entry has been given up on, with the reason a reader would want.</summary>
    public string? AbandonedReason { get; set; }

    public Device? Device { get; set; }
}
