using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_SystemAndOrganizationModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_modules",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_module = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                    short_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_modules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_system_modules_is_active",
                schema: "platform",
                table: "system_modules",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_system_modules_key_module",
                schema: "platform",
                table: "system_modules",
                column: "key_module",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO platform.system_modules (id, key_module, display_name, description, short_label, is_active, created_at_utc, updated_at_utc)
                VALUES
                    ('00000000-0000-0000-0002-000000000001'::uuid, 'hive-social', 'Hive Social', 'Content operations', 'Social', true, '2026-05-23T00:00:00Z'::timestamptz, NOW()),
                    ('00000000-0000-0000-0002-000000000002'::uuid, 'hive-ecommerce', 'Hive E-Commerce', 'Commerce operations', 'Commerce', true, '2026-05-15T00:00:00Z'::timestamptz, NOW())
                ON CONFLICT (key_module) DO NOTHING;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "module_id",
                schema: "platform",
                table: "organization_user_modules",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE platform.organization_user_modules oum
                SET module_id = sm.id
                FROM platform.system_modules sm
                WHERE LOWER(oum.module_key) = LOWER(sm.key_module);
                """);

            // If legacy rows reference unknown keys, drop them so the non-null FK conversion remains valid.
            migrationBuilder.Sql(
                """
                DELETE FROM platform.organization_user_modules
                WHERE module_id IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "organization_modules",
                schema: "platform",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_modules", x => new { x.organization_id, x.module_id });
                    table.ForeignKey(
                        name: "fk_organization_modules_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_modules_system_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "platform",
                        principalTable: "system_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO platform.organization_modules (organization_id, module_id, sort_order, created_at_utc, updated_at_utc)
                SELECT
                    organization_id,
                    module_id,
                    MIN(sort_order) AS sort_order,
                    NOW(),
                    NOW()
                FROM platform.organization_user_modules
                GROUP BY organization_id, module_id;
                """);

            migrationBuilder.DropPrimaryKey(
                name: "pk_organization_user_modules",
                schema: "platform",
                table: "organization_user_modules");

            migrationBuilder.DropColumn(
                name: "module_key",
                schema: "platform",
                table: "organization_user_modules");

            migrationBuilder.AlterColumn<Guid>(
                name: "module_id",
                schema: "platform",
                table: "organization_user_modules",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_organization_user_modules",
                schema: "platform",
                table: "organization_user_modules",
                columns: new[] { "organization_id", "user_id", "module_id" });

            migrationBuilder.CreateIndex(
                name: "ix_organization_user_modules_module_id",
                schema: "platform",
                table: "organization_user_modules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_modules_module_id",
                schema: "platform",
                table: "organization_modules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_modules_org_sort_order",
                schema: "platform",
                table: "organization_modules",
                columns: new[] { "organization_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_organization_user_modules_system_modules_module_id",
                schema: "platform",
                table: "organization_user_modules",
                column: "module_id",
                principalSchema: "platform",
                principalTable: "system_modules",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_organization_user_modules_system_modules_module_id",
                schema: "platform",
                table: "organization_user_modules");

            migrationBuilder.DropPrimaryKey(
                name: "pk_organization_user_modules",
                schema: "platform",
                table: "organization_user_modules");

            migrationBuilder.DropIndex(
                name: "ix_organization_user_modules_module_id",
                schema: "platform",
                table: "organization_user_modules");

            migrationBuilder.AddColumn<string>(
                name: "module_key",
                schema: "platform",
                table: "organization_user_modules",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE platform.organization_user_modules oum
                SET module_key = sm.key_module
                FROM platform.system_modules sm
                WHERE oum.module_id = sm.id;
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM platform.organization_user_modules
                WHERE module_key IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "module_key",
                schema: "platform",
                table: "organization_user_modules",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "module_id",
                schema: "platform",
                table: "organization_user_modules");

            migrationBuilder.AddPrimaryKey(
                name: "pk_organization_user_modules",
                schema: "platform",
                table: "organization_user_modules",
                columns: new[] { "organization_id", "user_id", "module_key" });

            migrationBuilder.DropTable(
                name: "organization_modules",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "system_modules",
                schema: "platform");
        }
    }
}
