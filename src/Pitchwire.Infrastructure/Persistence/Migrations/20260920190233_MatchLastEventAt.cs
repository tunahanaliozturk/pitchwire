using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pitchwire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchLastEventAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_event_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_matches_status_last_event_at",
                table: "matches",
                columns: new[] { "status", "last_event_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_matches_status_last_event_at",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "last_event_at",
                table: "matches");
        }
    }
}
