using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_ContentWorkflowSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_workflow_sessions",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    config_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_workflow_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_workflow_sessions_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "social",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_content_workflow_sessions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_workflow_candidates",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_result_id = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_selected = table.Column<bool>(type: "boolean", nullable: false),
                    composition_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    enqueue_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    queued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_workflow_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_workflow_candidates_content_workflow_sessions_sessi~",
                        column: x => x.session_id,
                        principalSchema: "social",
                        principalTable: "content_workflow_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_workflow_events",
                schema: "social",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_workflow_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_workflow_events_content_workflow_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "social",
                        principalTable: "content_workflow_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_workflow_candidates_session_enqueue_key_active",
                schema: "social",
                table: "content_workflow_candidates",
                columns: new[] { "session_id", "enqueue_key" },
                unique: true,
                filter: "status NOT IN ('AnalysisFailed', 'BatchFailed', 'BatchStarted') AND enqueue_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_content_workflow_candidates_session_id",
                schema: "social",
                table: "content_workflow_candidates",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_workflow_events_session_created",
                schema: "social",
                table: "content_workflow_events",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_content_workflow_sessions_client_id",
                schema: "social",
                table: "content_workflow_sessions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_workflow_sessions_org_client_created",
                schema: "social",
                table: "content_workflow_sessions",
                columns: new[] { "organization_id", "client_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_workflow_candidates",
                schema: "social");

            migrationBuilder.DropTable(
                name: "content_workflow_events",
                schema: "social");

            migrationBuilder.DropTable(
                name: "content_workflow_sessions",
                schema: "social");
        }
    }
}
