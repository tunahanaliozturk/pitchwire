using Microsoft.EntityFrameworkCore;
using Pitchwire.Api.Domain;

namespace Pitchwire.Api.Persistence;

public sealed class PitchwireDbContext(DbContextOptions<PitchwireDbContext> options) : DbContext(options)
{
    public DbSet<League> Leagues => Set<League>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Player> Players => Set<Player>();

    public DbSet<TeamSeason> TeamSeasons => Set<TeamSeason>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<MatchEvent> MatchEvents => Set<MatchEvent>();

    public DbSet<Standing> Standings => Set<Standing>();

    public DbSet<PlayerSeasonStats> PlayerSeasonStats => Set<PlayerSeasonStats>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<League>(league =>
        {
            league.HasIndex(x => x.Slug).IsUnique();
            league.Property(x => x.Name).HasMaxLength(120);
            league.Property(x => x.Country).HasMaxLength(80);
            league.Property(x => x.Slug).HasMaxLength(120);
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

        modelBuilder.Entity<PlayerSeasonStats>(stats =>
        {
            stats.HasKey(x => new { x.SeasonId, x.PlayerId });
            stats.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId);
            stats.HasIndex(x => new { x.SeasonId, x.Goals });
        });
    }
}
