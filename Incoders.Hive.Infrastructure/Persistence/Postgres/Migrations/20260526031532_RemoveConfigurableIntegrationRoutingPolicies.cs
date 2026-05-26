using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveConfigurableIntegrationRoutingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_integration_routing_policy_items",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "organization_integration_routing_policies",
                schema: "platform");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_integration_routing_policies",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    flow_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    platform_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                name: "organization_integration_routing_policy_items",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allow_managed_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    connection_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    selected_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    timeout_ms = table.Column<int>(type: "integer", nullable: false),
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
        }
    }
}
