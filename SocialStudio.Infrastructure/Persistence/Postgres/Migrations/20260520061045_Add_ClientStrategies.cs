using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ClientStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_strategies",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_pillars = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    tone_keywords = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    target_audience = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    posting_frequency = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_strategies", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_strategies_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_strategies_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monthly_objectives",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    objectives = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    kpis = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monthly_objectives", x => x.id);
                    table.ForeignKey(
                        name: "fk_monthly_objectives_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_monthly_objectives_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_strategies_one_per_client",
                schema: "social",
                table: "client_strategies",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_strategies_organization_id",
                schema: "social",
                table: "client_strategies",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_monthly_objectives_client_id",
                schema: "social",
                table: "monthly_objectives",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_monthly_objectives_client_year_month",
                schema: "social",
                table: "monthly_objectives",
                columns: new[] { "client_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monthly_objectives_organization_id",
                schema: "social",
                table: "monthly_objectives",
                column: "organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_strategies",
                schema: "social");

            migrationBuilder.DropTable(
                name: "monthly_objectives",
                schema: "social");
        }
    }
}
