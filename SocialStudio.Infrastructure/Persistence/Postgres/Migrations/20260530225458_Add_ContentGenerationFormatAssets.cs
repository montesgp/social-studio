using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ContentGenerationFormatAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_generation_format_assets",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    format_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_index = table.Column<int>(type: "integer", nullable: false),
                    storage_path = table.Column<string>(type: "text", nullable: false),
                    filename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_generation_format_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_generation_format_assets_content_generation_format_~",
                        column: x => x.format_result_id,
                        principalSchema: "social",
                        principalTable: "content_generation_format_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_format_assets_format_result_id",
                schema: "social",
                table: "content_generation_format_assets",
                column: "format_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_format_assets_result_index",
                schema: "social",
                table: "content_generation_format_assets",
                columns: new[] { "format_result_id", "asset_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_format_assets_storage_path",
                schema: "social",
                table: "content_generation_format_assets",
                column: "storage_path");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_generation_format_assets",
                schema: "social");
        }
    }
}
