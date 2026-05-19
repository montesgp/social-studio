using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ClientBrandManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_brand_manuals",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    color_palette = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    logos = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    background_templates = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    typography = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    voice_and_tone = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_brand_manuals", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_brand_manuals_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_brand_manuals_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_brand_manuals_one_active_per_client",
                schema: "social",
                table: "client_brand_manuals",
                column: "client_id",
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_client_brand_manuals_organization_id",
                schema: "social",
                table: "client_brand_manuals",
                column: "organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_brand_manuals",
                schema: "social");
        }
    }
}
