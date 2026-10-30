using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ContentGenerationSocialTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_generation_jobs",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_formats = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    brand_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_generation_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_generation_jobs_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_generation_format_results",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    output_json = table.Column<string>(type: "jsonb", nullable: true),
                    composition_spec_json = table.Column<string>(type: "jsonb", nullable: true),
                    rendered_asset_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    inferred_fields_json = table.Column<string>(type: "jsonb", nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_generation_format_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_generation_format_results_content_generation_jobs_j~",
                        column: x => x.job_id,
                        principalSchema: "social",
                        principalTable: "content_generation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generated_packages",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generated_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_generated_packages_content_generation_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "social",
                        principalTable: "content_generation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_format_results_job_format",
                schema: "social",
                table: "content_generation_format_results",
                columns: new[] { "job_id", "format_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_jobs_analysis_id",
                schema: "social",
                table: "content_generation_jobs",
                column: "analysis_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_jobs_org_created",
                schema: "social",
                table: "content_generation_jobs",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_content_generation_jobs_status",
                schema: "social",
                table: "content_generation_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_generated_packages_job_id",
                schema: "social",
                table: "generated_packages",
                column: "job_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_generation_format_results",
                schema: "social");

            migrationBuilder.DropTable(
                name: "generated_packages",
                schema: "social");

            migrationBuilder.DropTable(
                name: "content_generation_jobs",
                schema: "social");
        }
    }
}
