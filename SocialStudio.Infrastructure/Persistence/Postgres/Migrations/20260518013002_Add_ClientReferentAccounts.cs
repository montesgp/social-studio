using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ClientReferentAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_referent_accounts",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_network_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    handle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_referent_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_referent_accounts_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_referent_accounts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_referent_accounts_social_networks_social_network_id",
                        column: x => x.social_network_id,
                        principalSchema: "platform",
                        principalTable: "social_networks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_referent_accounts_client_id",
                schema: "social",
                table: "client_referent_accounts",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_referent_accounts_client_network_url",
                schema: "social",
                table: "client_referent_accounts",
                columns: new[] { "client_id", "social_network_id", "profile_url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_referent_accounts_organization_id",
                schema: "social",
                table: "client_referent_accounts",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_referent_accounts_social_network_id",
                schema: "social",
                table: "client_referent_accounts",
                column: "social_network_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_referent_accounts",
                schema: "social");
        }
    }
}
