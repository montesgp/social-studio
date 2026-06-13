using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOrganizationIntegrationDefaultAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_default_ai",
                schema: "platform",
                table: "organization_integrations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_default_ai",
                schema: "platform",
                table: "organization_integrations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
