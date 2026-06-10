using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurableWorkflowIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_ai_models",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    capabilities_json = table.Column<string>(type: "text", nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_ai_models", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_ai_models_integration_providers_provider_id",
                        column: x => x.provider_id,
                        principalSchema: "platform",
                        principalTable: "integration_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_ai_models_organization_integrations_organizati~",
                        column: x => x.organization_integration_id,
                        principalSchema: "platform",
                        principalTable: "organization_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_ai_models_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_workflow_step_configs",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    analysis_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    step_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    operation_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    network_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    organization_integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    actor_id = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_workflow_step_configs", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_workflow_step_configs_integration_providers_pr~",
                        column: x => x.provider_id,
                        principalSchema: "platform",
                        principalTable: "integration_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_workflow_step_configs_organization_integration~",
                        column: x => x.organization_integration_id,
                        principalSchema: "platform",
                        principalTable: "organization_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_workflow_step_configs_organizations_organizati~",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organization_ai_models_org_provider_model",
                schema: "platform",
                table: "organization_ai_models",
                columns: new[] { "organization_id", "provider_id", "model_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_ai_models_organization_integration_id",
                schema: "platform",
                table: "organization_ai_models",
                column: "organization_integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_ai_models_provider_id",
                schema: "platform",
                table: "organization_ai_models",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_workflow_step_configs_org_workflow_analysis",
                schema: "platform",
                table: "organization_workflow_step_configs",
                columns: new[] { "organization_id", "workflow_key", "analysis_type" });

            migrationBuilder.CreateIndex(
                name: "ix_org_workflow_step_configs_step_priority",
                schema: "platform",
                table: "organization_workflow_step_configs",
                columns: new[] { "organization_id", "workflow_key", "analysis_type", "step_key", "network_key", "operation_key", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_workflow_step_configs_organization_integration~",
                schema: "platform",
                table: "organization_workflow_step_configs",
                column: "organization_integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_workflow_step_configs_provider_id",
                schema: "platform",
                table: "organization_workflow_step_configs",
                column: "provider_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_ai_models",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "organization_workflow_step_configs",
                schema: "platform");
        }
    }
}
