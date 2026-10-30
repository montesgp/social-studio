using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageActionStateAndAiRetryGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ai_analysis_failed_attempt_count",
                schema: "social",
                table: "single_content_analyses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ai_analysis_retry_blocked_at_utc",
                schema: "social",
                table: "single_content_analyses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "canva_send_count",
                schema: "social",
                table: "generated_packages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "download_count",
                schema: "social",
                table: "generated_packages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_downloaded_at_utc",
                schema: "social",
                table: "generated_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_downloaded_by_user_id",
                schema: "social",
                table: "generated_packages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_sent_to_canva_at_utc",
                schema: "social",
                table: "generated_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_sent_to_canva_by_user_id",
                schema: "social",
                table: "generated_packages",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_analysis_failed_attempt_count",
                schema: "social",
                table: "single_content_analyses");

            migrationBuilder.DropColumn(
                name: "ai_analysis_retry_blocked_at_utc",
                schema: "social",
                table: "single_content_analyses");

            migrationBuilder.DropColumn(
                name: "canva_send_count",
                schema: "social",
                table: "generated_packages");

            migrationBuilder.DropColumn(
                name: "download_count",
                schema: "social",
                table: "generated_packages");

            migrationBuilder.DropColumn(
                name: "last_downloaded_at_utc",
                schema: "social",
                table: "generated_packages");

            migrationBuilder.DropColumn(
                name: "last_downloaded_by_user_id",
                schema: "social",
                table: "generated_packages");

            migrationBuilder.DropColumn(
                name: "last_sent_to_canva_at_utc",
                schema: "social",
                table: "generated_packages");

            migrationBuilder.DropColumn(
                name: "last_sent_to_canva_by_user_id",
                schema: "social",
                table: "generated_packages");
        }
    }
}
