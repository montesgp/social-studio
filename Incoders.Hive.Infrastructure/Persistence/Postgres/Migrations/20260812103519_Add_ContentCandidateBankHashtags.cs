using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ContentCandidateBankHashtags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "bank_hashtags",
                schema: "social",
                table: "discovered_candidates",
                type: "text[]",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_discovered_candidates_bank",
                schema: "social",
                table: "discovered_candidates",
                columns: new[] { "organization_id", "network_key", "client_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_discovered_candidates_bank",
                schema: "social",
                table: "discovered_candidates");

            migrationBuilder.DropColumn(
                name: "bank_hashtags",
                schema: "social",
                table: "discovered_candidates");
        }
    }
}
