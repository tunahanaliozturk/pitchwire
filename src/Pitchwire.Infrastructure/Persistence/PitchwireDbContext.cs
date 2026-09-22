using Microsoft.EntityFrameworkCore;
using Pitchwire.Application.Persistence;
using Pitchwire.Domain;

namespace Pitchwire.Infrastructure.Persistence;

public sealed class PitchwireDbContext(DbContextOptions<PitchwireDbContext> options)
    : DbContext(options), IPitchwireDbContext
{
    public DbSet<Country> Countries => Set<Country>();

    public DbSet<League> Leagues => Set<League>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Player> Players => Set<Player>();

    public DbSet<TeamSeason> TeamSeasons => Set<TeamSeason>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<MatchEvent> MatchEvents => Set<MatchEvent>();

    public DbSet<Standing> Standings => Set<Standing>();

    public DbSet<PlayerSeasonStats> PlayerSeasonStats => Set<PlayerSeasonStats>();

    public DbSet<MatchTeamSheet> MatchTeamSheets => Set<MatchTeamSheet>();

    public DbSet<MatchLineupEntry> MatchLineups => Set<MatchLineupEntry>();

    public DbSet<MatchStatistics> MatchStatistics => Set<MatchStatistics>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<DeviceFavourite> DeviceFavourites => Set<DeviceFavourite>();

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    public DbSet<NotificationOutboxEntry> NotificationOutbox => Set<NotificationOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Country>(country =>
        {
            country.Property(x => x.Name).HasMaxLength(80);
            country.Property(x => x.Code).HasMaxLength(2).IsFixedLength();
            country.Property(x => x.Slug).HasMaxLength(80);
            country.HasIndex(x => x.Slug).IsUnique();
            country.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<League>(league =>
        {
            league.HasIndex(x => x.Slug).IsUnique();
            league.Property(x => x.Name).HasMaxLength(120);
            league.Property(x => x.Slug).HasMaxLength(120);
            league.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId);

            // A country's leagues are listed by tier, which is this pair.
            league.HasIndex(x => new { x.CountryId, x.Tier });
        });

        modelBuilder.Entity<Season>(season =>
        {
            season.HasIndex(x => new { x.LeagueId, x.Year }).IsUnique();
            season.HasOne(x => x.League).WithMany().HasForeignKey(x => x.LeagueId);
        });

        modelBuilder.Entity<Team>(team =>
        {
            team.HasIndex(x => x.Slug).IsUnique();
            team.Property(x => x.Name).HasMaxLength(120);
            team.Property(x => x.ShortName).HasMaxLength(24);
            team.Property(x => x.Slug).HasMaxLength(120);
        });

        modelBuilder.Entity<Player>(player =>
        {
            player.Property(x => x.Name).HasMaxLength(120);
            player.Property(x => x.Position).HasConversion<string>().HasMaxLength(16);
            player.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId);
            player.HasIndex(x => x.TeamId);
        });

        modelBuilder.Entity<TeamSeason>(participation =>
        {
            participation.HasKey(x => new { x.SeasonId, x.TeamId });
            participation.HasOne(x => x.Season).WithMany().HasForeignKey(x => x.SeasonId);
            participation.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId);
        });

        modelBuilder.Entity<Match>(match =>
        {
            match.HasOne(x => x.Season).WithMany().HasForeignKey(x => x.SeasonId);
            match.HasOne(x => x.HomeTeam).WithMany().HasForeignKey(x => x.HomeTeamId).OnDelete(DeleteBehavior.Restrict);
            match.HasOne(x => x.AwayTeam).WithMany().HasForeignKey(x => x.AwayTeamId).OnDelete(DeleteBehavior.Restrict);

            // A status stored as a number is unreadable in psql at two in the morning, and the bytes
            // saved are irrelevant at this size.
            match.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

            match.HasIndex(x => new { x.SeasonId, x.Round });

            // The live list reads by status and the fixture list pages on kickoff time. The second
            // index carries the identifier because that pair is the keyset the pagination token holds.
            match.HasIndex(x => x.Status);

            // The sweeper looks for live matches that have gone quiet, which is this pair.
            match.HasIndex(x => new { x.Status, x.LastEventAt });
            match.HasIndex(x => new { x.KickoffUtc, x.Id });
        });

        modelBuilder.Entity<MatchEvent>(@event =>
        {
            @event.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
            @event.Property(x => x.Provider).HasMaxLength(64);
            @event.Property(x => x.ProviderEventId).HasMaxLength(128);
            @event.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);

            // Idempotency lives here. An existence check in application code cannot hold, because two
            // concurrent requests can both pass it and both insert.
            @event.HasIndex(x => new { x.Provider, x.ProviderEventId })
                .IsUnique()
                .HasDatabaseName("ix_match_events_provider_event");

            // Ordering checks and the repair pull both read a match's events by sequence.
            @event.HasIndex(x => new { x.MatchId, x.Sequence });
        });

        modelBuilder.Entity<Standing>(standing =>
        {
            standing.HasKey(x => new { x.SeasonId, x.TeamId });
            standing.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId);
            standing.Ignore(x => x.GoalDifference);

            // The table is read in table order. Points and goals decide it, and the difference is
            // computed from the two columns already here.
            standing.HasIndex(x => new { x.SeasonId, x.Points, x.GoalsFor });
        });

        modelBuilder.Entity<MatchTeamSheet>(sheet =>
        {
            sheet.HasKey(x => new { x.MatchId, x.TeamId });
            sheet.Property(x => x.Formation).HasMaxLength(16);
        });

        modelBuilder.Entity<MatchLineupEntry>(entry =>
        {
            entry.HasKey(x => new { x.MatchId, x.TeamId, x.PlayerId });
            entry.Property(x => x.Position).HasConversion<string>().HasMaxLength(16);
            entry.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId);
        });

        modelBuilder.Entity<MatchStatistics>(stats =>
        {
            // One row per side, replaced by each newer snapshot. The history is not worth keeping: the
            // numbers are cumulative, so every older row is contained in the newest one.
            stats.HasKey(x => new { x.MatchId, x.TeamId });
        });

        modelBuilder.Entity<Device>(device =>
        {
            device.Property(x => x.TokenHash).HasMaxLength(64);
            device.Property(x => x.TimeZoneId).HasMaxLength(64);

            // The lookup on every request that carries a device cookie, so it is the one index this
            // table cannot do without.
            device.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<DeviceFavourite>(favourite =>
        {
            favourite.HasKey(x => new { x.DeviceId, x.TeamId });
            favourite.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
            favourite.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId);

            // The fan out reads this the other way round: given a team, who follows it.
            favourite.HasIndex(x => x.TeamId);
        });

        modelBuilder.Entity<PushSubscription>(subscription =>
        {
            subscription.Property(x => x.Endpoint).HasMaxLength(512);
            subscription.Property(x => x.P256dh).HasMaxLength(256);
            subscription.Property(x => x.Auth).HasMaxLength(256);
            subscription.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);

            // A browser that subscribes twice sends the same endpoint, and two rows for it would mean
            // two notifications for one goal.
            subscription.HasIndex(x => x.Endpoint).IsUnique();
        });

        modelBuilder.Entity<NotificationOutboxEntry>(entry =>
        {
            entry.Property(x => x.Title).HasMaxLength(120);
            entry.Property(x => x.Body).HasMaxLength(300);
            entry.Property(x => x.AbandonedReason).HasMaxLength(200);
            entry.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);

            // One notification per device per event. A redelivered goal that somehow reached the fan
            // out twice buzzes a phone once.
            entry.HasIndex(x => new { x.DeviceId, x.MatchEventId }).IsUnique();

            // What the relay claims: unsent work that is due, oldest first.
            entry.HasIndex(x => new { x.SentAt, x.NextAttemptAt });
        });

        modelBuilder.Entity<PlayerSeasonStats>(stats =>
        {
            stats.HasKey(x => new { x.SeasonId, x.PlayerId });
            stats.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId);
            stats.HasIndex(x => new { x.SeasonId, x.Goals });
        });
    }
}
