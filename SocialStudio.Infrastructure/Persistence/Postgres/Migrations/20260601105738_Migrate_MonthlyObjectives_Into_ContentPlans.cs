using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialStudio.Infrastructure.Persistence.Postgres.Migrations
{
    /// <summary>
    /// Data migration: copies existing rows from social.client_monthly_objectives into
    /// social.content_plans as Objective nodes. Period kind = 0 (Monthly).
    /// Objective Provenance is set to Source=0 (Manual), Status=1 (Draft) — all existing rows
    /// are assumed to be manually authored (predating AI Provenance tracking).
    /// Content Strategy and Calendar nodes are left as Empty provenance (Source=0, Status=0).
    ///
    /// *** ROLLBACK PAUSE BOUNDARY ***
    /// After applying this migration, verify data in staging BEFORE applying
    /// Drop_ClientMonthlyObjectives. Do NOT apply that migration until this step is confirmed correct.
    /// </summary>
    public partial class Migrate_MonthlyObjectives_Into_ContentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ProvenanceSource.Manual = 0, ProvenanceStatus.Draft = 1, ProvenanceStatus.Empty = 0
            // PeriodKind.Monthly = 0
            migrationBuilder.Sql(@"
INSERT INTO social.content_plans (
    id,
    client_id,
    organization_id,
    period_kind,
    year,
    month,
    objective_text,
    objective_kpis,
    objective_provenance_source,
    objective_provenance_status,
    objective_provenance_generated_at_utc,
    objective_provenance_prompt_version,
    content_strategy_text,
    content_strategy_provenance_source,
    content_strategy_provenance_status,
    content_strategy_provenance_generated_at_utc,
    content_strategy_provenance_prompt_version,
    calendar_provenance_source,
    calendar_provenance_status,
    calendar_provenance_generated_at_utc,
    calendar_provenance_prompt_version,
    created_at_utc,
    updated_at_utc
)
SELECT
    gen_random_uuid(),
    o.client_id,
    o.organization_id,
    0            AS period_kind,
    o.year,
    o.month,
    o.objectives AS objective_text,
    o.kpis       AS objective_kpis,
    0            AS objective_provenance_source,
    1            AS objective_provenance_status,
    NULL         AS objective_provenance_generated_at_utc,
    NULL         AS objective_provenance_prompt_version,
    NULL         AS content_strategy_text,
    0            AS content_strategy_provenance_source,
    0            AS content_strategy_provenance_status,
    NULL         AS content_strategy_provenance_generated_at_utc,
    NULL         AS content_strategy_provenance_prompt_version,
    0            AS calendar_provenance_source,
    0            AS calendar_provenance_status,
    NULL         AS calendar_provenance_generated_at_utc,
    NULL         AS calendar_provenance_prompt_version,
    o.created_at_utc,
    o.updated_at_utc
FROM social.client_monthly_objectives o
ON CONFLICT (client_id, period_kind, year, month) DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove rows inserted by this migration only (period_kind=0, matching source objectives).
            migrationBuilder.Sql(@"
DELETE FROM social.content_plans cp
WHERE cp.period_kind = 0
  AND EXISTS (
      SELECT 1 FROM social.client_monthly_objectives o
      WHERE o.client_id = cp.client_id
        AND o.year = cp.year
        AND o.month = cp.month
  );
");
        }
    }
}
