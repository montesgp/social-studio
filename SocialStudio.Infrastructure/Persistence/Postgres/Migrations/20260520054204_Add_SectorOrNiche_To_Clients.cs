using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_SectorOrNiche_To_Clients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sector_or_niche",
                schema: "social",
                table: "clients",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                schema: "social",
                table: "client_referent_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_client_referent_accounts_client_sort_order",
                schema: "social",
                table: "client_referent_accounts",
                columns: new[] { "client_id", "sort_order" });

            // Backfill: assign sort_order based on created_at_utc ASC per client (0-based)
            migrationBuilder.Sql("""
                UPDATE social.client_referent_accounts ra
                SET sort_order = sub.rn - 1
                FROM (
                    SELECT id, ROW_NUMBER() OVER (PARTITION BY client_id ORDER BY created_at_utc ASC) AS rn
                    FROM social.client_referent_accounts
                ) sub
                WHERE ra.id = sub.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_client_referent_accounts_client_sort_order",
                schema: "social",
                table: "client_referent_accounts");

            migrationBuilder.DropColumn(
                name: "sector_or_niche",
                schema: "social",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "sort_order",
                schema: "social",
                table: "client_referent_accounts");
        }
    }
}
