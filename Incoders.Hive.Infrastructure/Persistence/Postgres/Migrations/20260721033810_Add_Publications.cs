using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_Publications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_account_connections",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_network_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_account_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    encrypted_access_token = table.Column<string>(type: "text", nullable: false),
                    token_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    last_tested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_account_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_account_connections_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_account_connections_organization_integrations_organi~",
                        column: x => x.organization_integration_id,
                        principalSchema: "platform",
                        principalTable: "organization_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_account_connections_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_client_account_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_social_network_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_network_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    scheduled_for_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    external_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publications", x => x.id);
                    table.ForeignKey(
                        name: "fk_publications_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_publications_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publication_attempts",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<short>(type: "smallint", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publication_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_publication_attempts_publications_publication_id",
                        column: x => x.publication_id,
                        principalSchema: "social",
                        principalTable: "publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_account_connections_client_id",
                schema: "social",
                table: "client_account_connections",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_account_connections_org_client",
                schema: "social",
                table: "client_account_connections",
                columns: new[] { "organization_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_client_account_connections_org_client_network_unique",
                schema: "social",
                table: "client_account_connections",
                columns: new[] { "organization_id", "client_id", "social_network_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_account_connections_organization_integration_id",
                schema: "social",
                table: "client_account_connections",
                column: "organization_integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_publication_attempts_publication_attempt_number",
                schema: "social",
                table: "publication_attempts",
                columns: new[] { "publication_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publication_attempts_publication_id",
                schema: "social",
                table: "publication_attempts",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_publications_client",
                schema: "social",
                table: "publications",
                columns: new[] { "organization_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_publications_client_id",
                schema: "social",
                table: "publications",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_publications_content_item",
                schema: "social",
                table: "publications",
                column: "content_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_publications_scheduled",
                schema: "social",
                table: "publications",
                columns: new[] { "status", "scheduled_for_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_publications_status",
                schema: "social",
                table: "publications",
                columns: new[] { "organization_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_account_connections",
                schema: "social");

            migrationBuilder.DropTable(
                name: "publication_attempts",
                schema: "social");

            migrationBuilder.DropTable(
                name: "publications",
                schema: "social");
        }
    }
}
