using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_NicheCatalogAndClientNiches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "niches",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    icon_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    search_hashtags = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_niches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_niches",
                schema: "social",
                columns: table => new
                {
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    niche_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_niches", x => new { x.client_id, x.niche_id });
                    table.ForeignKey(
                        name: "fk_client_niches_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_niches_niches_niche_id",
                        column: x => x.niche_id,
                        principalSchema: "platform",
                        principalTable: "niches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_niches_niche_id",
                schema: "social",
                table: "client_niches",
                column: "niche_id");

            migrationBuilder.CreateIndex(
                name: "ix_niches_code",
                schema: "platform",
                table: "niches",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_niches_created_at_utc",
                schema: "platform",
                table: "niches",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_niches_display_name",
                schema: "platform",
                table: "niches",
                column: "display_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_niches",
                schema: "social");

            migrationBuilder.DropTable(
                name: "niches",
                schema: "platform");
        }
    }
}
