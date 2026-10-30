using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleUserOrganizationAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH user_org_activity AS (
                    SELECT
                        user_id,
                        organization_id,
                        MAX(updated_at_utc) AS last_module_updated_at,
                        MAX(created_at_utc) AS last_module_created_at,
                        NULL::timestamptz AS membership_created_at
                    FROM platform.organization_user_modules
                    GROUP BY user_id, organization_id

                    UNION ALL

                    SELECT
                        user_id,
                        organization_id,
                        NULL::timestamptz AS last_module_updated_at,
                        NULL::timestamptz AS last_module_created_at,
                        MAX(created_at_utc) AS membership_created_at
                    FROM platform.organization_memberships
                    GROUP BY user_id, organization_id
                ),
                ranked_user_orgs AS (
                    SELECT
                        user_id,
                        organization_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY user_id
                            ORDER BY
                                MAX(last_module_updated_at) DESC NULLS LAST,
                                MAX(last_module_created_at) DESC NULLS LAST,
                                MAX(membership_created_at) DESC NULLS LAST,
                                organization_id
                        ) AS rank
                    FROM user_org_activity
                    GROUP BY user_id, organization_id
                )
                DELETE FROM platform.organization_user_modules modules
                USING ranked_user_orgs keep
                WHERE keep.user_id = modules.user_id
                  AND keep.rank = 1
                  AND modules.organization_id <> keep.organization_id;
                """);

            migrationBuilder.Sql(
                """
                WITH user_org_activity AS (
                    SELECT
                        user_id,
                        organization_id,
                        MAX(updated_at_utc) AS last_module_updated_at,
                        MAX(created_at_utc) AS last_module_created_at,
                        NULL::timestamptz AS membership_created_at
                    FROM platform.organization_user_modules
                    GROUP BY user_id, organization_id

                    UNION ALL

                    SELECT
                        user_id,
                        organization_id,
                        NULL::timestamptz AS last_module_updated_at,
                        NULL::timestamptz AS last_module_created_at,
                        MAX(created_at_utc) AS membership_created_at
                    FROM platform.organization_memberships
                    GROUP BY user_id, organization_id
                ),
                ranked_user_orgs AS (
                    SELECT
                        user_id,
                        organization_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY user_id
                            ORDER BY
                                MAX(last_module_updated_at) DESC NULLS LAST,
                                MAX(last_module_created_at) DESC NULLS LAST,
                                MAX(membership_created_at) DESC NULLS LAST,
                                organization_id
                        ) AS rank
                    FROM user_org_activity
                    GROUP BY user_id, organization_id
                )
                DELETE FROM platform.organization_memberships memberships
                USING ranked_user_orgs keep
                WHERE keep.user_id = memberships.user_id
                  AND keep.rank = 1
                  AND memberships.organization_id <> keep.organization_id;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO platform.organization_memberships (organization_id, user_id, role, created_at_utc)
                SELECT DISTINCT
                    modules.organization_id,
                    modules.user_id,
                    'Member',
                    NOW()
                FROM platform.organization_user_modules modules
                LEFT JOIN platform.organization_memberships memberships
                    ON memberships.organization_id = modules.organization_id
                   AND memberships.user_id = modules.user_id
                WHERE memberships.user_id IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_organization_memberships_user_id_unique",
                schema: "platform",
                table: "organization_memberships",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_organization_user_modules_membership",
                schema: "platform",
                table: "organization_user_modules",
                columns: new[] { "organization_id", "user_id" },
                principalSchema: "platform",
                principalTable: "organization_memberships",
                principalColumns: new[] { "organization_id", "user_id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_organization_user_modules_membership",
                schema: "platform",
                table: "organization_user_modules");

            migrationBuilder.DropIndex(
                name: "ix_organization_memberships_user_id_unique",
                schema: "platform",
                table: "organization_memberships");
        }
    }
}
