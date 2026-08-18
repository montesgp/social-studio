using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Fix_NicheDiscoveryEvidenceUniquePerOpportunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_niche_discovery_evidence_run_kind_ref",
                schema: "social",
                table: "niche_discovery_evidence");

            migrationBuilder.CreateIndex(
                name: "ux_niche_discovery_evidence_run_opportunity_kind_ref",
                schema: "social",
                table: "niche_discovery_evidence",
                columns: new[] { "run_id", "opportunity_id", "evidence_kind", "external_ref" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_niche_discovery_evidence_run_opportunity_kind_ref",
                schema: "social",
                table: "niche_discovery_evidence");

            migrationBuilder.CreateIndex(
                name: "ux_niche_discovery_evidence_run_kind_ref",
                schema: "social",
                table: "niche_discovery_evidence",
                columns: new[] { "run_id", "evidence_kind", "external_ref" },
                unique: true);
        }
    }
}
