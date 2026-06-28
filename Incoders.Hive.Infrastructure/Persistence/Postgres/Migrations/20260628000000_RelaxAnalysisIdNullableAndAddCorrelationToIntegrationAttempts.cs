using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <summary>
    /// Makes <c>analysis_id</c> nullable on <c>social.integration_execution_attempts</c> and
    /// adds <c>correlation_id</c> / <c>correlation_kind</c> columns.  Rows produced by
    /// background services (e.g. <c>ClientAestheticImageBackgroundService</c>) carry a
    /// <c>CorrelationId</c> instead of an <c>AnalysisId</c>.
    /// </summary>
    public partial class RelaxAnalysisIdNullableAndAddCorrelationToIntegrationAttempts : Migration
    {
        private const string Schema = "social";
        private const string Table = "integration_execution_attempts";
        private const string FkName = "fk_integration_execution_attempts_single_content_analyses_anal~";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the existing required FK so the column can be altered.
            migrationBuilder.DropForeignKey(
                name: FkName,
                schema: Schema,
                table: Table);

            // 2. Relax analysis_id to nullable.
            migrationBuilder.AlterColumn<Guid>(
                name: "analysis_id",
                schema: Schema,
                table: Table,
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // 3. Add per-slide correlation columns.
            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: Schema,
                table: Table,
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation_kind",
                schema: Schema,
                table: Table,
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // 4. Re-add the FK as optional (nullable FK is allowed in Postgres).
            migrationBuilder.AddForeignKey(
                name: FkName,
                schema: Schema,
                table: Table,
                column: "analysis_id",
                principalSchema: Schema,
                principalTable: "single_content_analyses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Remove new columns.
            migrationBuilder.DropColumn(name: "correlation_kind", schema: Schema, table: Table);
            migrationBuilder.DropColumn(name: "correlation_id", schema: Schema, table: Table);

            // 2. Drop the optional FK.
            migrationBuilder.DropForeignKey(name: FkName, schema: Schema, table: Table);

            // 3. Make analysis_id NOT NULL again.  Any rows with null analysis_id must be
            //    deleted or back-filled before running Down in production.
            migrationBuilder.AlterColumn<Guid>(
                name: "analysis_id",
                schema: Schema,
                table: Table,
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // 4. Re-add the required FK.
            migrationBuilder.AddForeignKey(
                name: FkName,
                schema: Schema,
                table: Table,
                column: "analysis_id",
                principalSchema: Schema,
                principalTable: "single_content_analyses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
