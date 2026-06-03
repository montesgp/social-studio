using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profile_analyses",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    own_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_url_or_handle = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    raw_posts_dataset_json = table.Column<string>(type: "jsonb", nullable: true),
                    server_derived_stats_json = table.Column<string>(type: "jsonb", nullable: true),
                    ai_analysis_json = table.Column<string>(type: "jsonb", nullable: true),
                    provider_diagnostics_json = table.Column<string>(type: "jsonb", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_analyses", x => x.id);
                    table.ForeignKey(
                        name: "fk_profile_analyses_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profile_analyses_org_client_id",
                schema: "social",
                table: "profile_analyses",
                columns: new[] { "organization_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_profile_analyses_org_created_at",
                schema: "social",
                table: "profile_analyses",
                columns: new[] { "organization_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_analyses",
                schema: "social");
        }
    }
}
