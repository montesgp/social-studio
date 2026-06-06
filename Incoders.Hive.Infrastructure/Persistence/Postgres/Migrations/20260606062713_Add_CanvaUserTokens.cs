using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_CanvaUserTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "canva_user_tokens",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    encrypted_access_token = table.Column<string>(type: "text", nullable: false),
                    encrypted_refresh_token = table.Column<string>(type: "text", nullable: false),
                    access_token_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    scopes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_canva_user_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_canva_user_tokens_organization_integration",
                        column: x => x.organization_integration_id,
                        principalSchema: "platform",
                        principalTable: "organization_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_canva_user_tokens_org_integration_id",
                schema: "platform",
                table: "canva_user_tokens",
                column: "organization_integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_canva_user_tokens_user_org_integration",
                schema: "platform",
                table: "canva_user_tokens",
                columns: new[] { "user_id", "organization_integration_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canva_user_tokens",
                schema: "platform");
        }
    }
}
