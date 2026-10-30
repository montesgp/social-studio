using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSingleAnalyzerRoutingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_provider_capabilities",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    platform_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_provider_capabilities", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_provider_capabilities_integration_providers_pro~",
                        column: x => x.provider_id,
                        principalSchema: "platform",
                        principalTable: "integration_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_integration_routing_policies",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capability_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    platform_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_integration_routing_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_integration_routing_policies_organizations_org~",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "single_content_analyses",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    normalized_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    source_platform_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_snapshot_json = table.Column<string>(type: "text", nullable: true),
                    analysis_json = table.Column<string>(type: "text", nullable: true),
                    provider_diagnostics_json = table.Column<string>(type: "text", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    failure_message_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_single_content_analyses", x => x.id);
                    table.ForeignKey(
                        name: "fk_single_content_analyses_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_integration_routing_policy_items",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    allow_managed_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    timeout_ms = table.Column<int>(type: "integer", nullable: false),
                    selected_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_integration_routing_policy_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_integration_routing_policy_items_integration_p~",
                        column: x => x.provider_id,
                        principalSchema: "platform",
                        principalTable: "integration_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_integration_routing_policy_items_organization_~",
                        column: x => x.policy_id,
                        principalSchema: "platform",
                        principalTable: "organization_integration_routing_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integration_execution_attempts",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    elapsed_ms = table.Column<int>(type: "integer", nullable: false),
                    diagnostics_json = table.Column<string>(type: "text", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_execution_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_execution_attempts_single_content_analyses_anal~",
                        column: x => x.analysis_id,
                        principalSchema: "social",
                        principalTable: "single_content_analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_attempts_analysis_capability_provider_attempt",
                schema: "social",
                table: "integration_execution_attempts",
                columns: new[] { "analysis_id", "capability_key", "provider_key", "attempt_number" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_provider_capabilities_provider_capability_global",
                schema: "platform",
                table: "integration_provider_capabilities",
                columns: new[] { "provider_id", "capability_key" },
                unique: true,
                filter: "platform_key IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_integration_provider_capabilities_provider_capability_platform",
                schema: "platform",
                table: "integration_provider_capabilities",
                columns: new[] { "provider_id", "capability_key", "platform_key" },
                unique: true,
                filter: "platform_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_org_routing_policies_org_flow_capability_global",
                schema: "platform",
                table: "organization_integration_routing_policies",
                columns: new[] { "organization_id", "flow_key", "capability_key" },
                unique: true,
                filter: "platform_key IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_org_routing_policies_org_flow_capability_platform",
                schema: "platform",
                table: "organization_integration_routing_policies",
                columns: new[] { "organization_id", "flow_key", "capability_key", "platform_key" },
                unique: true,
                filter: "platform_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_org_routing_policy_items_policy_priority",
                schema: "platform",
                table: "organization_integration_routing_policy_items",
                columns: new[] { "policy_id", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_integration_routing_policy_items_provider_id",
                schema: "platform",
                table: "organization_integration_routing_policy_items",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_single_content_analyses_org_platform",
                schema: "social",
                table: "single_content_analyses",
                columns: new[] { "organization_id", "source_platform_key" });

            migrationBuilder.CreateIndex(
                name: "ix_single_content_analyses_org_status",
                schema: "social",
                table: "single_content_analyses",
                columns: new[] { "organization_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_execution_attempts",
                schema: "social");

            migrationBuilder.DropTable(
                name: "integration_provider_capabilities",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "organization_integration_routing_policy_items",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "single_content_analyses",
                schema: "social");

            migrationBuilder.DropTable(
                name: "organization_integration_routing_policies",
                schema: "platform");
        }
    }
}
