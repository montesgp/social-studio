using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_providers",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    supports_bring_your_own_key = table.Column<bool>(type: "boolean", nullable: false),
                    supports_managed_by_incoders = table.Column<bool>(type: "boolean", nullable: false),
                    supports_system_default = table.Column<bool>(type: "boolean", nullable: false),
                    system_default_read_only = table.Column<bool>(type: "boolean", nullable: false),
                    credential_fields_json = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organization_integrations",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_read_only = table.Column<bool>(type: "boolean", nullable: false),
                    is_default_ai = table.Column<bool>(type: "boolean", nullable: false),
                    selected_ai_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_tested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_integrations_integration_providers_provider_id",
                        column: x => x.provider_id,
                        principalSchema: "platform",
                        principalTable: "integration_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_integrations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_integration_credentials",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    encrypted_value = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_integration_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_integration_credentials_organization_integrati~",
                        column: x => x.organization_integration_id,
                        principalSchema: "platform",
                        principalTable: "organization_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_providers_key",
                schema: "platform",
                table: "integration_providers",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_integration_credentials_organization_integrati~",
                schema: "platform",
                table: "organization_integration_credentials",
                columns: new[] { "organization_integration_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_integrations_organization_id_provider_id",
                schema: "platform",
                table: "organization_integrations",
                columns: new[] { "organization_id", "provider_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_integrations_organization_id_status",
                schema: "platform",
                table: "organization_integrations",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_organization_integrations_provider_id",
                schema: "platform",
                table: "organization_integrations",
                column: "provider_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_integration_credentials",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "organization_integrations",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "integration_providers",
                schema: "platform");
        }
    }
}
