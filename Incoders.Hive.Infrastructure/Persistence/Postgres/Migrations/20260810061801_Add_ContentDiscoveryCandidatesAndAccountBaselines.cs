using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ContentDiscoveryCandidatesAndAccountBaselines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_baselines",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    network_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    author_handle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    median_views = table.Column<double>(type: "double precision", nullable: true),
                    median_engagement = table.Column<double>(type: "double precision", nullable: true),
                    post_count = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_baselines", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_baselines_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discovered_candidates",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    network_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    search_criterion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    author_handle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    views = table.Column<long>(type: "bigint", nullable: true),
                    likes = table.Column<long>(type: "bigint", nullable: true),
                    comments = table.Column<long>(type: "bigint", nullable: true),
                    shares = table.Column<long>(type: "bigint", nullable: true),
                    saves = table.Column<long>(type: "bigint", nullable: true),
                    opportunity_score = table.Column<double>(type: "double precision", nullable: true),
                    classification = table.Column<short>(type: "smallint", nullable: true),
                    observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovered_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_discovered_candidates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_account_baselines_org_network_handle",
                schema: "social",
                table: "account_baselines",
                columns: new[] { "organization_id", "network_key", "author_handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discovered_candidates_org_client_key",
                schema: "social",
                table: "discovered_candidates",
                columns: new[] { "organization_id", "client_id", "candidate_key" });

            migrationBuilder.CreateIndex(
                name: "ix_discovered_candidates_org_observed",
                schema: "social",
                table: "discovered_candidates",
                columns: new[] { "organization_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_discovered_candidates_key_observed",
                schema: "social",
                table: "discovered_candidates",
                columns: new[] { "organization_id", "client_id", "candidate_key", "observed_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_baselines",
                schema: "social");

            migrationBuilder.DropTable(
                name: "discovered_candidates",
                schema: "social");
        }
    }
}
