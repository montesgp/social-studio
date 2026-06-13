using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Remove_ToneKeywords_From_GeneralStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tone_keywords",
                schema: "social",
                table: "general_strategies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tone_keywords",
                schema: "social",
                table: "general_strategies",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'");
        }
    }
}
