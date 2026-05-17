using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class MovePlatformTablesToPlatformSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenant_memberships_tenants_tenant_id",
                table: "tenant_memberships");

            migrationBuilder.DropPrimaryKey(
                name: "pk_tenants",
                table: "tenants");

            migrationBuilder.DropPrimaryKey(
                name: "pk_tenant_memberships",
                table: "tenant_memberships");

            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.RenameTable(
                name: "system_settings",
                newName: "system_settings",
                newSchema: "platform");

            migrationBuilder.RenameTable(
                name: "tenants",
                newName: "organizations",
                newSchema: "platform");

            migrationBuilder.RenameTable(
                name: "tenant_memberships",
                newName: "organization_memberships",
                newSchema: "platform");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                schema: "platform",
                table: "system_settings",
                newName: "organization_id");

            migrationBuilder.RenameIndex(
                name: "ix_system_settings_scope_tenant_id_user_id_key",
                schema: "platform",
                table: "system_settings",
                newName: "ix_system_settings_scope_organization_id_user_id_key");

            migrationBuilder.RenameIndex(
                name: "ix_tenants_name",
                schema: "platform",
                table: "organizations",
                newName: "ix_organizations_name");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                schema: "platform",
                table: "organization_memberships",
                newName: "organization_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_organizations",
                schema: "platform",
                table: "organizations",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_organization_memberships",
                schema: "platform",
                table: "organization_memberships",
                columns: new[] { "organization_id", "user_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_organization_memberships_organizations_organization_id",
                schema: "platform",
                table: "organization_memberships",
                column: "organization_id",
                principalSchema: "platform",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_organization_memberships_organizations_organization_id",
                schema: "platform",
                table: "organization_memberships");

            migrationBuilder.DropPrimaryKey(
                name: "pk_organizations",
                schema: "platform",
                table: "organizations");

            migrationBuilder.DropPrimaryKey(
                name: "pk_organization_memberships",
                schema: "platform",
                table: "organization_memberships");

            migrationBuilder.RenameTable(
                name: "system_settings",
                schema: "platform",
                newName: "system_settings");

            migrationBuilder.RenameTable(
                name: "organizations",
                schema: "platform",
                newName: "tenants");

            migrationBuilder.RenameTable(
                name: "organization_memberships",
                schema: "platform",
                newName: "tenant_memberships");

            migrationBuilder.RenameColumn(
                name: "organization_id",
                table: "system_settings",
                newName: "tenant_id");

            migrationBuilder.RenameIndex(
                name: "ix_system_settings_scope_organization_id_user_id_key",
                table: "system_settings",
                newName: "ix_system_settings_scope_tenant_id_user_id_key");

            migrationBuilder.RenameIndex(
                name: "ix_organizations_name",
                table: "tenants",
                newName: "ix_tenants_name");

            migrationBuilder.RenameColumn(
                name: "organization_id",
                table: "tenant_memberships",
                newName: "tenant_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_tenants",
                table: "tenants",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_tenant_memberships",
                table: "tenant_memberships",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_memberships_tenants_tenant_id",
                table: "tenant_memberships",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
