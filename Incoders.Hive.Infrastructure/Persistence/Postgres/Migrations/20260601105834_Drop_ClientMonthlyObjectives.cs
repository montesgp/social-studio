using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <summary>
    /// Cleanup migration: drops social.client_monthly_objectives and its indexes.
    /// Apply ONLY after Migrate_MonthlyObjectives_Into_ContentPlans has been verified in staging.
    /// This migration is a SEPARATE step from the data move (task 5.5) to allow a safe pause
    /// between data migration verification and the irreversible table drop.
    ///
    /// Down() recreates the table so the migration can be rolled back if needed before final sign-off.
    /// </summary>
    public partial class Drop_ClientMonthlyObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_monthly_objectives",
                schema: "social");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_monthly_objectives",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    objectives = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    kpis = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_monthly_objectives", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_monthly_objectives_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_monthly_objectives_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_monthly_objectives_client_id",
                schema: "social",
                table: "client_monthly_objectives",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_monthly_objectives_client_year_month",
                schema: "social",
                table: "client_monthly_objectives",
                columns: new[] { "client_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_monthly_objectives_organization_id",
                schema: "social",
                table: "client_monthly_objectives",
                column: "organization_id");
        }
    }
}
