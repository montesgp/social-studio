using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_OrganizationActorCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_actors",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: false),
                    is_owned = table.Column<bool>(type: "boolean", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_actors", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_actors_integration_providers_provider_id",
                        column: x => x.provider_id,
                        principalSchema: "platform",
                        principalTable: "integration_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_actors_organization_integrations_organization_~",
                        column: x => x.organization_integration_id,
                        principalSchema: "platform",
                        principalTable: "organization_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_actors_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organization_actors_org_provider_actor",
                schema: "platform",
                table: "organization_actors",
                columns: new[] { "organization_id", "provider_id", "actor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_actors_organization_integration_id",
                schema: "platform",
                table: "organization_actors",
                column: "organization_integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_actors_provider_id",
                schema: "platform",
                table: "organization_actors",
                column: "provider_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_actors",
                schema: "platform");
        }
    }
}
