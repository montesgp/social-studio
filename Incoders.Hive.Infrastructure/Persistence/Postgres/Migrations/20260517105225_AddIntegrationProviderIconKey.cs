using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationProviderIconKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "icon_key",
                schema: "platform",
                table: "integration_providers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "icon_key",
                schema: "platform",
                table: "integration_providers");
        }
    }
}
