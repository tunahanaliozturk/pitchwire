namespace Pitchwire.Domain;

/// <summary>
/// One browser, and what it has asked to be told about.
/// </summary>
/// <remarks>
/// There is no account behind this. A device is identified by a token it was handed on its first
/// visit, and everything it chose belongs to it alone. That is a deliberate limit: favourites do not
/// follow a person to a second device, and clearing site data loses them.
/// <para>
/// Preferences live on this row rather than in a table of their own. They are one to one with the
/// device and always read together with it, and a second table would only add a join to every
/// notification decision.
/// </para>
/// </remarks>
public sealed class Device
{
    public Guid Id { get; init; }

    /// <summary>
    /// SHA-256 of the token the browser holds. The token itself is never stored and never logged: a
    /// database that leaks would otherwise hand over every device's identity ready to use.
    /// </summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// The device's own zone, as an IANA identifier. Quiet hours mean nothing without it: ten at night
    /// is a different instant in Istanbul than it is in London, and the server's clock knows neither.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    public TimeOnly? QuietHoursStart { get; set; }

    public TimeOnly? QuietHoursEnd { get; set; }

    public bool NotifyOnGoal { get; set; } = true;

    public bool NotifyOnRedCard { get; set; }

    public bool NotifyOnKickoff { get; set; }

    public bool NotifyOnFullTime { get; set; } = true;
}

/// <summary>
/// A team a device follows.
/// </summary>
public sealed class DeviceFavourite
{
    public Guid DeviceId { get; set; }

    public Guid TeamId { get; set; }

    public Device? Device { get; set; }

    public Team? Team { get; set; }
}

/// <summary>
/// Where a browser can be reached when its tab is closed.
/// </summary>
/// <remarks>
/// A subscription dies without telling anybody: the browser is uninstalled, the user clears their
/// data, the push service expires it. The failure count is how that is noticed, and a subscription
/// the push service refuses permanently is deleted rather than retried forever.
/// </remarks>
public sealed class PushSubscription
{
    public Guid Id { get; init; }

    public Guid DeviceId { get; set; }

    public required string Endpoint { get; set; }

    public required string P256dh { get; set; }

    public required string Auth { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int FailureCount { get; set; }

    public Device? Device { get; set; }
}
