using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <summary>
    /// Additive migration: creates social.content_plans and social.content_plan_slots.
    /// No data is moved or dropped in this step. Unique index on (client_id, period_kind, year, month)
    /// preserves the monthly Period key constraint.
    /// </summary>
    public partial class Add_ContentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_plans",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_kind = table.Column<short>(type: "smallint", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    objective_text = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    objective_kpis = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    objective_provenance_source = table.Column<short>(type: "smallint", nullable: false),
                    objective_provenance_status = table.Column<short>(type: "smallint", nullable: false),
                    objective_provenance_generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    objective_provenance_prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    content_strategy_text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    content_strategy_provenance_source = table.Column<short>(type: "smallint", nullable: false),
                    content_strategy_provenance_status = table.Column<short>(type: "smallint", nullable: false),
                    content_strategy_provenance_generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    content_strategy_provenance_prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    calendar_provenance_source = table.Column<short>(type: "smallint", nullable: false),
                    calendar_provenance_status = table.Column<short>(type: "smallint", nullable: false),
                    calendar_provenance_generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    calendar_provenance_prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_plans_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_content_plans_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_plan_slots",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    content_format = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    produced_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    slot_provenance_source = table.Column<short>(type: "smallint", nullable: false),
                    slot_provenance_status = table.Column<short>(type: "smallint", nullable: false),
                    slot_provenance_generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    slot_provenance_prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_plan_slots", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_plan_slots_content_plans_content_plan_id",
                        column: x => x.content_plan_id,
                        principalSchema: "social",
                        principalTable: "content_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_plan_slots_plan_id",
                schema: "social",
                table: "content_plan_slots",
                column: "content_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_plan_slots_plan_sort_order",
                schema: "social",
                table: "content_plan_slots",
                columns: new[] { "content_plan_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_content_plans_client_id",
                schema: "social",
                table: "content_plans",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_plans_client_period",
                schema: "social",
                table: "content_plans",
                columns: new[] { "client_id", "period_kind", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_plans_organization_id",
                schema: "social",
                table: "content_plans",
                column: "organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_plan_slots",
                schema: "social");

            migrationBuilder.DropTable(
                name: "content_plans",
                schema: "social");
        }
    }
}
