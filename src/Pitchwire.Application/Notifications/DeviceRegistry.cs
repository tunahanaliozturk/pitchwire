using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Persistence;
using Pitchwire.Domain;

namespace Pitchwire.Application.Notifications;

/// <summary>
/// Devices, the teams they follow, and where to reach them.
/// </summary>
/// <remarks>
/// There is no account here on purpose. A visitor gets a token on their first request and everything
/// they choose hangs off it, which means one click to follow a team instead of a sign up form. The
/// price is that favourites do not follow a person to a second device, and that is written down as a
/// known limitation rather than hidden.
/// </remarks>
public sealed class DeviceRegistry(IPitchwireDbContext db, IStoreFailures failures, TimeProvider clock)
{
    /// <summary>256 bits, which is well past what anybody could work through.</summary>
    private const int TokenBytes = 32;

    public async Task<(Device Device, string Token)> IssueAsync(CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = clock.GetUtcNow();

        var device = new Device
        {
            Id = Guid.CreateVersion7(),
            TokenHash = Hash(token),
            CreatedAt = now,
            LastSeenAt = now,
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync(cancellationToken);

        // The only time the token exists outside the browser holding it. What is stored is its hash,
        // so a leaked database hands over nothing anyone can use.
        return (device, token);
    }

    public async Task<Device?> FindAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = Hash(token);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.TokenHash == hash, cancellationToken);
        var now = clock.GetUtcNow();

        // Authentication is on every write, including one rejected by the rate limiter. Persisting
        // last-seen on every attempt would turn a cheap 429 into a database write under abuse.
        if (device is not null && now - device.LastSeenAt >= TimeSpan.FromMinutes(5))
        {
            device.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return device;
    }

    public async Task<IReadOnlyList<Guid>> FavouritesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await db.DeviceFavourites
            .AsNoTracking()
            .Where(favourite => favourite.DeviceId == deviceId)
            .Select(favourite => favourite.TeamId)
            .ToListAsync(cancellationToken);

    public async Task SetFavouritesAsync(Guid deviceId, IReadOnlyList<Guid> teamIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(teamIds);

        var wanted = teamIds.Distinct().ToHashSet();

        // Only teams that exist. A client sending an identifier for a team nobody has would otherwise
        // leave a row that can never be shown and can never be followed back to anything.
        var real = await db.Teams
            .Where(team => wanted.Contains(team.Id))
            .Select(team => team.Id)
            .ToListAsync(cancellationToken);

        var current = await db.DeviceFavourites
            .Where(favourite => favourite.DeviceId == deviceId)
            .ToListAsync(cancellationToken);

        foreach (var gone in current.Where(favourite => !real.Contains(favourite.TeamId)))
        {
            db.DeviceFavourites.Remove(gone);
        }

        foreach (var added in real.Where(teamId => !current.Any(favourite => favourite.TeamId == teamId)))
        {
            db.DeviceFavourites.Add(new DeviceFavourite { DeviceId = deviceId, TeamId = added });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SubscribeAsync(
        Guid deviceId,
        string endpoint,
        string p256dh,
        string auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

        if (existing is not null)
        {
            // An endpoint is a capability URL, not proof that the caller owns the browser that made
            // it. A different device must not be able to redirect its notifications to itself.
            if (existing.DeviceId != deviceId)
            {
                return false;
            }

            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.FailureCount = 0;
        }
        else
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.CreateVersion7(),
                DeviceId = deviceId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                CreatedAt = clock.GetUtcNow(),
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException failure) when (failures.IsUniqueViolation(failure))
        {
            // Two registrations can both miss the read above. The endpoint's unique index chooses
            // one owner; the loser gets the same conflict as a later takeover attempt.
            return false;
        }
    }

    public async Task UnsubscribeAsync(Guid deviceId, string endpoint, CancellationToken cancellationToken)
    {
        await db.PushSubscriptions
            .Where(s => s.DeviceId == deviceId && s.Endpoint == endpoint)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Writes changes a caller made to a tracked device. The alternative is a set of update methods
    /// that each take one field, and a preferences screen would need six of them.
    /// </summary>
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
