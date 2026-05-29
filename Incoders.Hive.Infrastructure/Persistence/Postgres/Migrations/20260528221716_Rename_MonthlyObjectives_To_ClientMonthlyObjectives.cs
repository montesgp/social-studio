using Incoders.Hive.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppIdentityDbContext))]
    [Migration("20260528221716_Rename_MonthlyObjectives_To_ClientMonthlyObjectives")]
    public partial class Rename_MonthlyObjectives_To_ClientMonthlyObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "monthly_objectives",
                schema: "social",
                newName: "client_monthly_objectives",
                newSchema: "social");

            migrationBuilder.Sql(
                "ALTER TABLE social.client_monthly_objectives RENAME CONSTRAINT pk_monthly_objectives TO pk_client_monthly_objectives;");

            migrationBuilder.Sql(
                "ALTER TABLE social.client_monthly_objectives RENAME CONSTRAINT fk_monthly_objectives_clients_client_id TO fk_client_monthly_objectives_clients_client_id;");

            migrationBuilder.Sql(
                "ALTER TABLE social.client_monthly_objectives RENAME CONSTRAINT fk_monthly_objectives_organizations_organization_id TO fk_client_monthly_objectives_organizations_organization_id;");

            migrationBuilder.RenameIndex(
                name: "ix_monthly_objectives_client_id",
                schema: "social",
                table: "client_monthly_objectives",
                newName: "ix_client_monthly_objectives_client_id");

            migrationBuilder.RenameIndex(
                name: "ix_monthly_objectives_client_year_month",
                schema: "social",
                table: "client_monthly_objectives",
                newName: "ix_client_monthly_objectives_client_year_month");

            migrationBuilder.RenameIndex(
                name: "ix_monthly_objectives_organization_id",
                schema: "social",
                table: "client_monthly_objectives",
                newName: "ix_client_monthly_objectives_organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_client_monthly_objectives_client_id",
                schema: "social",
                table: "client_monthly_objectives",
                newName: "ix_monthly_objectives_client_id");

            migrationBuilder.RenameIndex(
                name: "ix_client_monthly_objectives_client_year_month",
                schema: "social",
                table: "client_monthly_objectives",
                newName: "ix_monthly_objectives_client_year_month");

            migrationBuilder.RenameIndex(
                name: "ix_client_monthly_objectives_organization_id",
                schema: "social",
                table: "client_monthly_objectives",
                newName: "ix_monthly_objectives_organization_id");

            migrationBuilder.Sql(
                "ALTER TABLE social.client_monthly_objectives RENAME CONSTRAINT pk_client_monthly_objectives TO pk_monthly_objectives;");

            migrationBuilder.Sql(
                "ALTER TABLE social.client_monthly_objectives RENAME CONSTRAINT fk_client_monthly_objectives_clients_client_id TO fk_monthly_objectives_clients_client_id;");

            migrationBuilder.Sql(
                "ALTER TABLE social.client_monthly_objectives RENAME CONSTRAINT fk_client_monthly_objectives_organizations_organization_id TO fk_monthly_objectives_organizations_organization_id;");

            migrationBuilder.RenameTable(
                name: "client_monthly_objectives",
                schema: "social",
                newName: "monthly_objectives",
                newSchema: "social");
        }
    }
}
