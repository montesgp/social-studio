using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropIdentityTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.role_claims CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.user_tokens CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.user_roles CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.user_logins CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.user_claims CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.users CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS identity.roles CASCADE;");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS identity;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally not reversible — Identity tables are permanently removed.
        }
    }
}
