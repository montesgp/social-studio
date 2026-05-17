using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserPreferencesUserFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_preferences_users_user_id",
                schema: "platform",
                table: "user_preferences");

            migrationBuilder.Sql(
                "UPDATE platform.user_preferences SET theme_key = 'dark' WHERE theme_key = 'hive-core'");

            migrationBuilder.Sql(
                "UPDATE platform.user_preferences SET theme_key = 'light' WHERE theme_key = 'hive-social'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE platform.user_preferences SET theme_key = 'hive-core' WHERE theme_key = 'dark'");

            migrationBuilder.Sql(
                "UPDATE platform.user_preferences SET theme_key = 'hive-social' WHERE theme_key = 'light'");

            migrationBuilder.AddForeignKey(
                name: "fk_user_preferences_users_user_id",
                schema: "platform",
                table: "user_preferences",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
