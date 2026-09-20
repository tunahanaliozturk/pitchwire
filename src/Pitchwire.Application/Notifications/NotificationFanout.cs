using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Persistence;
using Pitchwire.Domain;

namespace Pitchwire.Application.Notifications;

/// <summary>
/// Decides who should hear about an event, and writes it down.
/// </summary>
/// <remarks>
/// This runs inside the ingestion transaction, alongside the event it reacts to. Sending from here
/// would notify people about a goal that a rollback then removed, and deciding afterwards without a
/// record would lose every notification the process was holding when it stopped.
/// </remarks>
public sealed partial class NotificationFanout(IPitchwireDbContext db, TimeProvider clock, ILogger<NotificationFanout> logger)
{
    public async Task QueueAsync(Match match, MatchEvent stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(stored);

        if (NotificationPolicy.TriggerFor(stored.Kind, stored.Sequence, match.Status) is not { } trigger)
        {
            return;
        }

        var followers = await db.DeviceFavourites
            .Where(favourite => favourite.TeamId == match.HomeTeamId || favourite.TeamId == match.AwayTeamId)
            .Select(favourite => favourite.Device!)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (followers.Count == 0)
        {
            return;
        }

        var names = await db.Teams
            .Where(team => team.Id == match.HomeTeamId || team.Id == match.AwayTeamId)
            .ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);

        var now = clock.GetUtcNow();
        var title = Title(trigger, match, stored, names);
        var body = Body(match, names);

        foreach (var device in followers)
        {
            // Quiet hours are the device's own, so the zone has to be resolved per device rather than
            // read off the server. A zone the device sent that this machine does not have is treated
            // as UTC and said out loud, because silently picking a zone would silence real
            // notifications at times nobody could explain.
            if (!NotificationPolicy.ShouldNotify(device, trigger, ZoneFor(device), now))
            {
                continue;
            }

            db.NotificationOutbox.Add(new NotificationOutboxEntry
            {
                Id = Guid.CreateVersion7(),
                DeviceId = device.Id,
                MatchEventId = stored.Id,
                MatchId = match.Id,
                Title = title,
                Body = body,
                CreatedAt = now,
                NextAttemptAt = now,
            });
        }
    }

    private static string Title(
        NotificationTrigger trigger,
        Match match,
        MatchEvent stored,
        Dictionary<Guid, string> names) => trigger switch
        {
            // An own goal names the team that benefited, because that is the team a reader follows and
            // the one whose score just moved.
            NotificationTrigger.Goal when stored.Kind == MatchEventKind.OwnGoal =>
                $"Own goal for {Name(names, stored.TeamId == match.HomeTeamId ? match.AwayTeamId : match.HomeTeamId)}",
            NotificationTrigger.Goal => $"Goal for {Name(names, stored.TeamId)}",
            NotificationTrigger.RedCard => $"Red card for {Name(names, stored.TeamId)}",
            NotificationTrigger.Kickoff => "Kick off",
            NotificationTrigger.FullTime => "Full time",
            _ => "Match update",
        };

    private static string Body(Match match, Dictionary<Guid, string> names) => string.Create(
        CultureInfo.InvariantCulture,
        $"{Name(names, match.HomeTeamId)} {match.HomeScore}-{match.AwayScore} {Name(names, match.AwayTeamId)} · {match.Minute}'");

    private static string Name(Dictionary<Guid, string> names, Guid teamId) =>
        names.TryGetValue(teamId, out var name) ? name : "Unknown";

    private TimeZoneInfo ZoneFor(Device device)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(device.TimeZoneId, out var zone))
        {
            return zone;
        }

        UnknownZone(logger, device.TimeZoneId);
        return TimeZoneInfo.Utc;
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning,
        Message = "A device reported the time zone {TimeZoneId}, which this machine does not have. Quiet hours will be read in UTC.")]
    private static partial void UnknownZone(ILogger logger, string timeZoneId);
}
