using Pitchwire.Domain;

namespace Pitchwire.UnitTests;

/// <summary>
/// When a device has asked not to be woken.
/// </summary>
/// <remarks>
/// The window people actually set crosses midnight, and that is the one a comparison written the
/// obvious way gets wrong. The failure is a phone buzzing at three in the morning, which nobody
/// reports as a bug and everybody remembers.
/// </remarks>
public sealed class QuietHoursTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    [Fact]
    public void Nothing_set_means_never_quiet()
    {
        QuietHours.IsQuiet(null, null, Istanbul, Instant("2026-09-20T02:00:00Z")).ShouldBeFalse();
        QuietHours.IsQuiet(new TimeOnly(22, 0), null, Istanbul, Instant("2026-09-20T02:00:00Z")).ShouldBeFalse();
        QuietHours.IsQuiet(null, new TimeOnly(8, 0), Istanbul, Instant("2026-09-20T02:00:00Z")).ShouldBeFalse();
    }

    [Fact]
    public void A_window_of_no_length_means_never_quiet()
    {
        // Rather than always quiet, which is what a range comparison would say and would silence the
        // product completely for anyone who set the two ends the same by accident.
        QuietHours.IsQuiet(new TimeOnly(22, 0), new TimeOnly(22, 0), Istanbul, Instant("2026-09-20T19:00:00Z"))
            .ShouldBeFalse();
    }

    [Fact]
    public void A_window_inside_one_day_holds_between_its_ends()
    {
        var start = new TimeOnly(13, 0);
        var end = new TimeOnly(15, 0);

        // 11:30 UTC is 14:30 in Istanbul, inside the window.
        QuietHours.IsQuiet(start, end, Istanbul, Instant("2026-09-20T11:30:00Z")).ShouldBeTrue();

        // 09:00 UTC is 12:00 there, before it.
        QuietHours.IsQuiet(start, end, Istanbul, Instant("2026-09-20T09:00:00Z")).ShouldBeFalse();

        // 13:00 UTC is 16:00 there, after it.
        QuietHours.IsQuiet(start, end, Istanbul, Instant("2026-09-20T13:00:00Z")).ShouldBeFalse();
    }

    [Fact]
    public void A_window_that_crosses_midnight_holds_on_both_sides_of_it()
    {
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(8, 0);

        // 20:00 UTC is 23:00 in Istanbul, the evening half of the window.
        QuietHours.IsQuiet(start, end, Istanbul, Instant("2026-09-20T20:00:00Z")).ShouldBeTrue();

        // 00:00 UTC is 03:00 there, the morning half.
        QuietHours.IsQuiet(start, end, Istanbul, Instant("2026-09-21T00:00:00Z")).ShouldBeTrue();

        // 15:00 UTC is 18:00 there, wide awake.
        QuietHours.IsQuiet(start, end, Istanbul, Instant("2026-09-20T15:00:00Z")).ShouldBeFalse();
    }

    [Fact]
    public void The_window_starts_at_its_start_and_ends_before_its_end()
    {
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(8, 0);

        // 19:00 UTC is exactly 22:00 in Istanbul.
        QuietHours.IsQuiet(start, end, Istanbul, Instant("2026-09-20T19:00:00Z")).ShouldBeTrue();

        // 05:00 UTC is exactly 08:00 there, which is when the quiet ends rather than a last minute of
        // it. Getting this edge wrong silences one notification a day and nobody can say which.
        QuietHours.IsQuiet(start, end, Istanbul, Instant("2026-09-21T05:00:00Z")).ShouldBeFalse();
    }

    [Fact]
    public void The_same_instant_is_quiet_in_one_zone_and_not_in_another()
    {
        // This is the reason the zone is stored per device rather than assumed. The server's own clock
        // is not an answer to what time it is where somebody is asleep.
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(8, 0);
        var instant = Instant("2026-09-20T20:30:00Z");

        QuietHours.IsQuiet(start, end, Istanbul, instant).ShouldBeTrue();
        QuietHours.IsQuiet(start, end, London, instant).ShouldBeFalse();
    }

    private static DateTimeOffset Instant(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
