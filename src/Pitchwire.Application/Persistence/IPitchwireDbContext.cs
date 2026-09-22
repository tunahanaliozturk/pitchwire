using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Pitchwire.Domain;

namespace Pitchwire.Application.Persistence;

/// <summary>
/// The store, as the application layer uses it.
/// </summary>
/// <remarks>
/// This is not a repository. It hands back the same <see cref="DbSet{TEntity}"/> the context holds, so
/// a use case keeps composable queries, projection straight into a result shape, and bulk updates,
/// none of which survive a generic repository with a FindAll and a GetById.
/// <para>
/// It exists for one reason: the layering. Without it the application layer would reference a database
/// provider, and the dependency arrow this repository claims to enforce would be enforced by nothing
/// but habit. It has one implementation, which is a cost of that choice rather than a benefit of it,
/// and it is recorded as such in the architecture decision record.
/// </para>
/// </remarks>
public interface IPitchwireDbContext
{
    DbSet<Country> Countries { get; }

    DbSet<League> Leagues { get; }

    DbSet<Season> Seasons { get; }

    DbSet<Team> Teams { get; }

    DbSet<Player> Players { get; }

    DbSet<TeamSeason> TeamSeasons { get; }

    DbSet<Match> Matches { get; }

    DbSet<MatchEvent> MatchEvents { get; }

    DbSet<Standing> Standings { get; }

    DbSet<PlayerSeasonStats> PlayerSeasonStats { get; }

    DbSet<MatchTeamSheet> MatchTeamSheets { get; }

    DbSet<MatchLineupEntry> MatchLineups { get; }

    DbSet<MatchStatistics> MatchStatistics { get; }

    DbSet<Device> Devices { get; }

    DbSet<DeviceFavourite> DeviceFavourites { get; }

    DbSet<PushSubscription> PushSubscriptions { get; }

    DbSet<NotificationOutboxEntry> NotificationOutbox { get; }

    /// <summary>Transactions and raw SQL, which the ingestion boundary needs for its row lock.</summary>
    DatabaseFacade Database { get; }

    EntityEntry Entry(object entity);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
