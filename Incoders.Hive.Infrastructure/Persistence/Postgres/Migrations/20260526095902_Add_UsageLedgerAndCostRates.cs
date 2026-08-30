using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Add_UsageLedgerAndCostRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_cost_rates",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    cost_per_1k_input_tokens = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    cost_per_1k_output_tokens = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    cost_per_request = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    effective_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_cost_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage_events",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workflow_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    pipeline_step_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    request_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    request_size_bytes = table.Column<int>(type: "integer", nullable: true),
                    response_size_bytes = table.Column<int>(type: "integer", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_cost_rates_provider_model_effective_from",
                schema: "platform",
                table: "provider_cost_rates",
                columns: new[] { "provider_key", "model", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_correlation_id",
                schema: "platform",
                table: "usage_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_org_client_timestamp",
                schema: "platform",
                table: "usage_events",
                columns: new[] { "organization_id", "client_id", "request_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_org_timestamp",
                schema: "platform",
                table: "usage_events",
                columns: new[] { "organization_id", "request_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_provider_model_timestamp",
                schema: "platform",
                table: "usage_events",
                columns: new[] { "provider_key", "model", "request_timestamp" });

            // Seed v1 cost rates — placeholder values based on public pricing as of 2026-05.
            // Notes column flags these as estimates; admin should verify before production billing.
            const string notes = "v1 seed; verify pricing before production";
            var effectiveFrom = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                schema: "platform",
                table: "provider_cost_rates",
                columns: new[] { "id", "provider_key", "model", "cost_per_1k_input_tokens", "cost_per_1k_output_tokens", "cost_per_request", "effective_from", "notes" },
                values: new object[,]
                {
                    // Claude — Anthropic public pricing (USD per 1K tokens)
                    { Guid.NewGuid(), "claude", "claude-sonnet-4-5",           0.00300000m, 0.01500000m, null, effectiveFrom, notes },
                    { Guid.NewGuid(), "claude", "claude-opus-4-7",             0.01500000m, 0.07500000m, null, effectiveFrom, notes },
                    { Guid.NewGuid(), "claude", "claude-haiku-4-5-20251001",   0.00080000m, 0.00400000m, null, effectiveFrom, notes },
                    // OpenAI public pricing
                    { Guid.NewGuid(), "openai", "gpt-4o",                      0.00250000m, 0.01000000m, null, effectiveFrom, notes },
                    { Guid.NewGuid(), "openai", "gpt-4o-mini",                 0.00015000m, 0.00060000m, null, effectiveFrom, notes },
                    // Gemini public pricing
                    { Guid.NewGuid(), "gemini", "gemini-2.0-flash",            0.00010000m, 0.00040000m, null, effectiveFrom, notes },
                    { Guid.NewGuid(), "gemini", "gemini-1.5-pro",              0.00125000m, 0.00500000m, null, effectiveFrom, notes },
                    // Apify — per-actor-task estimate; no token pricing
                    { Guid.NewGuid(), "apify",  null,                          null,        null,        0.00500000m, effectiveFrom, notes },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_cost_rates",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "usage_events",
                schema: "platform");
        }
    }
}
