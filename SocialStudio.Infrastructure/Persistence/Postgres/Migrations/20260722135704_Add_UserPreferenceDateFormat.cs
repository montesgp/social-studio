using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_UserPreferenceDateFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "date_format",
                schema: "platform",
                table: "user_preferences",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "dd/MM/yyyy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "date_format",
                schema: "platform",
                table: "user_preferences");
        }
    }
}
