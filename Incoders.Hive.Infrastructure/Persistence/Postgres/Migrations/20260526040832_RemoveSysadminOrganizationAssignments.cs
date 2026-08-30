using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSysadminOrganizationAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH sysadmin_users AS (
                    SELECT DISTINCT user_roles.user_id
                    FROM identity.user_roles user_roles
                    INNER JOIN identity.roles roles ON roles.id = user_roles.role_id
                    WHERE LOWER(roles.name) = 'sysadmin'
                )
                DELETE FROM platform.organization_user_modules modules
                USING sysadmin_users
                WHERE modules.user_id = sysadmin_users.user_id;
                """);

            migrationBuilder.Sql(
                """
                WITH sysadmin_users AS (
                    SELECT DISTINCT user_roles.user_id
                    FROM identity.user_roles user_roles
                    INNER JOIN identity.roles roles ON roles.id = user_roles.role_id
                    WHERE LOWER(roles.name) = 'sysadmin'
                )
                DELETE FROM platform.organization_memberships memberships
                USING sysadmin_users
                WHERE memberships.user_id = sysadmin_users.user_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
