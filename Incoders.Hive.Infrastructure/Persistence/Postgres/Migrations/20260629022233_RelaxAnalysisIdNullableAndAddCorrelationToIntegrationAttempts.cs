using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RelaxAnalysisIdNullableAndAddCorrelationToIntegrationAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "analysis_id",
                schema: "social",
                table: "integration_execution_attempts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "social",
                table: "integration_execution_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation_kind",
                schema: "social",
                table: "integration_execution_attempts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "social",
                table: "integration_execution_attempts");

            migrationBuilder.DropColumn(
                name: "correlation_kind",
                schema: "social",
                table: "integration_execution_attempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "analysis_id",
                schema: "social",
                table: "integration_execution_attempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
