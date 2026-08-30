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
            // Idempotency guard (issue #62): on a clean replay from empty, ReintroduceIdentityTables
            // drops+recreates identity.users, which cascades this FK away before we reach here. Using
            // DROP CONSTRAINT IF EXISTS keeps the chain replayable. The FK is being removed anyway, so
            // this is semantically identical. On production (already applied) EF never re-runs it.
            migrationBuilder.Sql(
                "ALTER TABLE platform.user_preferences DROP CONSTRAINT IF EXISTS fk_user_preferences_users_user_id;");

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
