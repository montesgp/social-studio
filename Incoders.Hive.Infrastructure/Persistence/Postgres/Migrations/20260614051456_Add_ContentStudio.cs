using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ContentStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_studio_items",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generator_kind = table.Column<short>(type: "smallint", nullable: false),
                    source_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    format_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_medium = table.Column<short>(type: "smallint", nullable: false),
                    draft_text = table.Column<string>(type: "text", nullable: true),
                    draft_visual_spec_markup = table.Column<string>(type: "text", nullable: true),
                    draft_assets = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    target_network = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_placement = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_state = table.Column<short>(type: "smallint", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variant_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary_variant = table.Column<bool>(type: "boolean", nullable: false),
                    edit_last_source = table.Column<short>(type: "smallint", nullable: false),
                    edit_last_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    edit_prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    plan_content_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_period_kind = table.Column<short>(type: "smallint", nullable: true),
                    plan_year = table.Column<int>(type: "integer", nullable: true),
                    plan_month = table.Column<int>(type: "integer", nullable: true),
                    plan_calendar_slot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_studio_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_studio_items_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_content_studio_items_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_studio_sets",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_kind = table.Column<short>(type: "smallint", nullable: false),
                    origin_generator_kind = table.Column<short>(type: "smallint", nullable: true),
                    origin_source_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_period_kind = table.Column<short>(type: "smallint", nullable: true),
                    origin_year = table.Column<int>(type: "integer", nullable: true),
                    origin_month = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_studio_sets", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_studio_sets_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_content_studio_sets_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_studio_set_items",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_studio_set_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_studio_set_items_content_studio_sets_set_id",
                        column: x => x.set_id,
                        principalSchema: "social",
                        principalTable: "content_studio_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_items_client_id",
                schema: "social",
                table: "content_studio_items",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_items_org_review_state",
                schema: "social",
                table: "content_studio_items",
                columns: new[] { "organization_id", "review_state" });

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_items_organization_id",
                schema: "social",
                table: "content_studio_items",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_items_source_run_id",
                schema: "social",
                table: "content_studio_items",
                column: "source_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_items_variant_group_id",
                schema: "social",
                table: "content_studio_items",
                column: "variant_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_set_items_item_id",
                schema: "social",
                table: "content_studio_set_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_set_items_set_item",
                schema: "social",
                table: "content_studio_set_items",
                columns: new[] { "set_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_sets_client_id",
                schema: "social",
                table: "content_studio_sets",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_sets_org_run_unique",
                schema: "social",
                table: "content_studio_sets",
                columns: new[] { "organization_id", "origin_source_run_id" },
                unique: true,
                filter: "origin_source_run_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_content_studio_sets_origin_source_run_id",
                schema: "social",
                table: "content_studio_sets",
                column: "origin_source_run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_studio_items",
                schema: "social");

            migrationBuilder.DropTable(
                name: "content_studio_set_items",
                schema: "social");

            migrationBuilder.DropTable(
                name: "content_studio_sets",
                schema: "social");
        }
    }
}
