namespace Pitchwire.Domain;

/// <summary>
/// Whether a device has asked not to be disturbed right now.
/// </summary>
/// <remarks>
/// The whole difficulty is the window that crosses midnight, which is the one people actually set.
/// Ten at night until eight in the morning is not a range in the arithmetic sense: the start is after
/// the end. A comparison written the obvious way says a device is never quiet, and the bug shows up
/// as a notification at three in the morning rather than as anything a test would fail on.
/// </remarks>
public static class QuietHours
{
    public static bool IsQuiet(TimeOnly? start, TimeOnly? end, TimeZoneInfo zone, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (start is not { } from || end is not { } until || from == until)
        {
            // Nothing set, or a window of zero length. Both mean the device never asked for quiet.
            return false;
        }

        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

        return from < until
            ? local >= from && local < until
            : local >= from || local < until;
    }
}
