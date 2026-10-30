using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <summary>
    /// Renames the EF entity from ClientStrategyData to GeneralStrategyData and renames the
    /// underlying table from social.client_strategies to social.general_strategies.
    /// The C# type rename means EF sees this as a new table; we use RenameTable to do it in-place.
    /// Also creates the new social.general_strategies table via EF model change and renames indexes.
    /// No data loss — rows are untouched, only the table name changes.
    /// </summary>
    public partial class Rename_ClientStrategies_To_GeneralStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename existing table — zero data change; all rows preserved.
            migrationBuilder.RenameTable(
                name: "client_strategies",
                schema: "social",
                newName: "general_strategies",
                newSchema: "social");

            // Rename the unique index on client_id
            migrationBuilder.RenameIndex(
                name: "ix_client_strategies_one_per_client",
                schema: "social",
                table: "general_strategies",
                newName: "ix_general_strategies_one_per_client");

            // Rename the organization_id index
            migrationBuilder.RenameIndex(
                name: "ix_client_strategies_organization_id",
                schema: "social",
                table: "general_strategies",
                newName: "ix_general_strategies_organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_general_strategies_organization_id",
                schema: "social",
                table: "client_strategies",
                newName: "ix_client_strategies_organization_id");

            migrationBuilder.RenameIndex(
                name: "ix_general_strategies_one_per_client",
                schema: "social",
                table: "client_strategies",
                newName: "ix_client_strategies_one_per_client");

            migrationBuilder.RenameTable(
                name: "general_strategies",
                schema: "social",
                newName: "client_strategies",
                newSchema: "social");
        }
    }
}
