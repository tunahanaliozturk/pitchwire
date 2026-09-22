using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pitchwire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CountriesSquadsStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "country",
                table: "leagues");

            migrationBuilder.AddColumn<string>(
                name: "position",
                table: "players",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "shirt_number",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_player_id",
                table: "match_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "country_id",
                table: "leagues",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "tier",
                table: "leagues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "countries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_countries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "match_lineups",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shirt_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_starter = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_lineups", x => new { x.match_id, x.team_id, x.player_id });
                    table.ForeignKey(
                        name: "fk_match_lineups_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_statistics",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of_minute = table.Column<int>(type: "integer", nullable: false),
                    possession = table.Column<int>(type: "integer", nullable: false),
                    shots = table.Column<int>(type: "integer", nullable: false),
                    shots_on_target = table.Column<int>(type: "integer", nullable: false),
                    corners = table.Column<int>(type: "integer", nullable: false),
                    fouls = table.Column<int>(type: "integer", nullable: false),
                    offsides = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_statistics", x => new { x.match_id, x.team_id });
                });

            migrationBuilder.CreateTable(
                name: "match_team_sheets",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    formation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_team_sheets", x => new { x.match_id, x.team_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_leagues_country_id_tier",
                table: "leagues",
                columns: new[] { "country_id", "tier" });

            migrationBuilder.CreateIndex(
                name: "ix_countries_code",
                table: "countries",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_countries_slug",
                table: "countries",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_match_lineups_player_id",
                table: "match_lineups",
                column: "player_id");

            // Everything that existed before this migration belonged to the one league this service
            // had, which is in England. The countries are inserted and the existing rows pointed at
            // the right one here, because the foreign key below would otherwise be added against an
            // empty identifier and refuse to apply to any database that already holds a league.
            //
            // The identifiers are the ones the catalogue derives from the country codes, so the
            // seeder recognises these rows as its own and does not insert them twice.
            migrationBuilder.Sql("""
                INSERT INTO countries (id, name, code, slug) VALUES
                    ('d27b027e-b8a4-61e5-269f-c100f6c5eaf0', 'England', 'GB', 'england'),
                    ('5139acdf-9bca-cdf9-58aa-74538d6f0417', 'Türkiye', 'TR', 'turkiye'),
                    ('1cf762d4-5782-b11e-e877-8387c04b61d8', 'Spain', 'ES', 'spain')
                ON CONFLICT (id) DO NOTHING;
                """);

            migrationBuilder.Sql("""
                UPDATE leagues
                SET country_id = 'd27b027e-b8a4-61e5-269f-c100f6c5eaf0', tier = 1
                WHERE country_id = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_leagues_countries_country_id",
                table: "leagues",
                column: "country_id",
                principalTable: "countries",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leagues_countries_country_id",
                table: "leagues");

            migrationBuilder.DropTable(
                name: "countries");

            migrationBuilder.DropTable(
                name: "match_lineups");

            migrationBuilder.DropTable(
                name: "match_statistics");

            migrationBuilder.DropTable(
                name: "match_team_sheets");

            migrationBuilder.DropIndex(
                name: "ix_leagues_country_id_tier",
                table: "leagues");

            migrationBuilder.DropColumn(
                name: "position",
                table: "players");

            migrationBuilder.DropColumn(
                name: "shirt_number",
                table: "players");

            migrationBuilder.DropColumn(
                name: "replaced_player_id",
                table: "match_events");

            migrationBuilder.DropColumn(
                name: "country_id",
                table: "leagues");

            migrationBuilder.DropColumn(
                name: "tier",
                table: "leagues");

            migrationBuilder.AddColumn<string>(
                name: "country",
                table: "leagues",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");
        }
    }
}
