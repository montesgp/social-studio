using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_NicheDiscoveryRunsOpportunitiesAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_runs",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_market_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_market_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    network_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    configuration_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    window_days = table.Column<int>(type: "integer", nullable: false),
                    depth = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    weights_json = table.Column<string>(type: "jsonb", nullable: false),
                    origin_source_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    unconfigured_source_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    failed_source_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    provider_units_spent = table.Column<int>(type: "integer", nullable: false),
                    ai_completions_spent = table.Column<int>(type: "integer", nullable: false),
                    semantic_degraded = table.Column<bool>(type: "boolean", nullable: false),
                    failure_reason_key = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_discovery_runs_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_catalog",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    display_name_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    primary_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_catalog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "niche_opportunities",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    niche_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sub_niche_label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    content_pattern = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    topic_labels = table.Column<List<string>>(type: "text[]", nullable: false),
                    opportunity_score = table.Column<double>(type: "double precision", nullable: false),
                    confidence_score = table.Column<double>(type: "double precision", nullable: false),
                    effective_weight_denominator = table.Column<double>(type: "double precision", nullable: false),
                    excluded_signals_json = table.Column<string>(type: "jsonb", nullable: false),
                    median_views = table.Column<long>(type: "bigint", nullable: true),
                    outlier_ratio = table.Column<double>(type: "double precision", nullable: true),
                    outlier_density = table.Column<double>(type: "double precision", nullable: true),
                    growth_proxy = table.Column<double>(type: "double precision", nullable: true),
                    cross_channel_repetition = table.Column<int>(type: "integer", nullable: true),
                    target_market_demand = table.Column<double>(type: "double precision", nullable: true),
                    target_market_competition = table.Column<double>(type: "double precision", nullable: true),
                    target_market_gap = table.Column<double>(type: "double precision", nullable: true),
                    automation_suitability = table.Column<double>(type: "double precision", nullable: true),
                    channel_sample_size = table.Column<int>(type: "integer", nullable: false),
                    video_sample_size = table.Column<int>(type: "integer", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_niche_opportunities", x => x.id);
                    table.ForeignKey(
                        name: "fk_niche_opportunities_discovery_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "social",
                        principalTable: "discovery_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_niche_opportunities_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "niche_discovery_evidence",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_kind = table.Column<short>(type: "smallint", nullable: false),
                    external_ref = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    channel_handle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    thumbnail_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    views = table.Column<long>(type: "bigint", nullable: true),
                    likes = table.Column<long>(type: "bigint", nullable: true),
                    comments = table.Column<long>(type: "bigint", nullable: true),
                    subscriber_count = table.Column<long>(type: "bigint", nullable: true),
                    total_views = table.Column<long>(type: "bigint", nullable: true),
                    video_count = table.Column<int>(type: "integer", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    language_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    source_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_niche_discovery_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_niche_discovery_evidence_discovery_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "social",
                        principalTable: "discovery_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_niche_discovery_evidence_niche_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalSchema: "social",
                        principalTable: "niche_opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_runs_org_created",
                schema: "social",
                table: "discovery_runs",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_market_catalog_code",
                schema: "social",
                table: "market_catalog",
                column: "market_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_niche_discovery_evidence_opportunity_id",
                schema: "social",
                table: "niche_discovery_evidence",
                column: "opportunity_id");

            migrationBuilder.CreateIndex(
                name: "ux_niche_discovery_evidence_run_kind_ref",
                schema: "social",
                table: "niche_discovery_evidence",
                columns: new[] { "run_id", "evidence_kind", "external_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_niche_opportunities_org_created",
                schema: "social",
                table: "niche_opportunities",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_niche_opportunities_run_rank",
                schema: "social",
                table: "niche_opportunities",
                columns: new[] { "run_id", "rank" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_catalog",
                schema: "social");

            migrationBuilder.DropTable(
                name: "niche_discovery_evidence",
                schema: "social");

            migrationBuilder.DropTable(
                name: "niche_opportunities",
                schema: "social");

            migrationBuilder.DropTable(
                name: "discovery_runs",
                schema: "social");
        }
    }
}
