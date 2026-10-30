using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ContentGenerationBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                schema: "social",
                table: "content_generation_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "batch_piece_index",
                schema: "social",
                table: "content_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "content_generation_batches",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    composition_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    total_pieces = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_generation_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_generation_batches_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_jobs_batch_id",
                schema: "social",
                table: "content_generation_jobs",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_batches_org_created",
                schema: "social",
                table: "content_generation_batches",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_batches_org_status",
                schema: "social",
                table: "content_generation_batches",
                columns: new[] { "organization_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_generation_batches",
                schema: "social");

            migrationBuilder.DropIndex(
                name: "ix_content_generation_jobs_batch_id",
                schema: "social",
                table: "content_generation_jobs");

            migrationBuilder.DropColumn(
                name: "batch_id",
                schema: "social",
                table: "content_generation_jobs");

            migrationBuilder.DropColumn(
                name: "batch_piece_index",
                schema: "social",
                table: "content_generation_jobs");
        }
    }
}
