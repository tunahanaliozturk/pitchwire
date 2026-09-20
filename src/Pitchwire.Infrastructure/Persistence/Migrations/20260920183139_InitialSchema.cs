using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pitchwire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leagues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leagues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    short_name = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasons", x => x.id);
                    table.ForeignKey(
                        name: "fk_seasons_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_players", x => x.id);
                    table.ForeignKey(
                        name: "fk_players_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "standings",
                columns: table => new
                {
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    played = table.Column<int>(type: "integer", nullable: false),
                    won = table.Column<int>(type: "integer", nullable: false),
                    drawn = table.Column<int>(type: "integer", nullable: false),
                    lost = table.Column<int>(type: "integer", nullable: false),
                    goals_for = table.Column<int>(type: "integer", nullable: false),
                    goals_against = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_standings", x => new { x.season_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_standings_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    kickoff_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    home_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    away_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    home_score = table.Column<int>(type: "integer", nullable: false),
                    away_score = table.Column<int>(type: "integer", nullable: false),
                    minute = table.Column<int>(type: "integer", nullable: false),
                    last_event_sequence = table.Column<int>(type: "integer", nullable: false),
                    is_degraded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_matches_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_matches_teams_away_team_id",
                        column: x => x.away_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_matches_teams_home_team_id",
                        column: x => x.home_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_seasons",
                columns: table => new
                {
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_seasons", x => new { x.season_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_team_seasons_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_team_seasons_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_season_stats",
                columns: table => new
                {
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goals = table.Column<int>(type: "integer", nullable: false),
                    assists = table.Column<int>(type: "integer", nullable: false),
                    yellow_cards = table.Column<int>(type: "integer", nullable: false),
                    red_cards = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_season_stats", x => new { x.season_id, x.player_id });
                    table.ForeignKey(
                        name: "fk_player_season_stats_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    minute = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assist_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_match_events_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leagues_slug",
                table: "leagues",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_match_events_match_id_sequence",
                table: "match_events",
                columns: new[] { "match_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_match_events_provider_event",
                table: "match_events",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_matches_away_team_id",
                table: "matches",
                column: "away_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_matches_home_team_id",
                table: "matches",
                column: "home_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_matches_kickoff_utc_id",
                table: "matches",
                columns: new[] { "kickoff_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_matches_season_id_round",
                table: "matches",
                columns: new[] { "season_id", "round" });

            migrationBuilder.CreateIndex(
                name: "ix_matches_status",
                table: "matches",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_player_season_stats_player_id",
                table: "player_season_stats",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_season_stats_season_id_goals",
                table: "player_season_stats",
                columns: new[] { "season_id", "goals" });

            migrationBuilder.CreateIndex(
                name: "ix_players_team_id",
                table: "players",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_seasons_league_id_year",
                table: "seasons",
                columns: new[] { "league_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_standings_season_id_points_goals_for",
                table: "standings",
                columns: new[] { "season_id", "points", "goals_for" });

            migrationBuilder.CreateIndex(
                name: "ix_standings_team_id",
                table: "standings",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_seasons_team_id",
                table: "team_seasons",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_slug",
                table: "teams",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_events");

            migrationBuilder.DropTable(
                name: "player_season_stats");

            migrationBuilder.DropTable(
                name: "standings");

            migrationBuilder.DropTable(
                name: "team_seasons");

            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropTable(
                name: "players");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "leagues");
        }
    }
}
