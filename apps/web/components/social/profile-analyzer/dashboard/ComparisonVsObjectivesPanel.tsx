"use client";

// ComparisonVsObjectivesPanel — PA-17: detected account state vs client objectives.
// Renders ONLY when clientId is non-null AND comparison data is present.
// Omitted entirely (not an empty panel) when clientId is null or no plan data exists (PA-17).

import { Badge } from "@/components/ui/badge";
import type { ProfileComparisonDto, ProfileAiAnalysisDto } from "@/lib/api/types/profile-analyzer";

// ── Types ─────────────────────────────────────────────────────────────────────

interface ComparisonVsObjectivesPanelProps {
  clientId: string | null;
  comparison: ProfileComparisonDto | null;
  aiAnalysis: ProfileAiAnalysisDto | null;
}

// ── Sub-components ────────────────────────────────────────────────────────────

function ComparisonRow({
  label,
  detected,
  stated,
}: {
  label: string;
  detected: React.ReactNode;
  stated: React.ReactNode;
}) {
  return (
    <div className="grid grid-cols-[1fr_1fr] gap-3 py-2 border-b border-border/20 last:border-0">
      <div className="space-y-0.5">
        <p className="text-xs text-muted-foreground/70 font-medium">{label} (detected)</p>
        <div className="text-sm text-foreground/80">{detected}</div>
      </div>
      <div className="space-y-0.5">
        <p className="text-xs text-muted-foreground/70 font-medium">{label} (target)</p>
        <div className="text-sm text-foreground/80">{stated}</div>
      </div>
    </div>
  );
}

function TagGroup({ items }: { items: string[] }) {
  if (items.length === 0)
    return <span className="text-xs text-muted-foreground/50">—</span>;
  return (
    <div className="flex flex-wrap gap-1">
      {items.map((item, i) => (
        <Badge key={i} variant="outline" className="text-xs font-normal">
          {item}
        </Badge>
      ))}
    </div>
  );
}

function TextOrDash({ text }: { text: string | null | undefined }) {
  if (!text) return <span className="text-xs text-muted-foreground/50">—</span>;
  return <span>{text}</span>;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ComparisonVsObjectivesPanel({
  clientId,
  comparison,
  aiAnalysis,
}: ComparisonVsObjectivesPanelProps) {
  // PA-17: omit entirely when clientId is null or no plan data exists
  if (!clientId || !comparison) {
    if (!clientId) {
      // URL-path: no comparison possible — do not render even a placeholder
      return null;
    }
    // clientId is set but no plan data: show subtle message
    return (
      <p className="text-xs text-muted-foreground/60 italic">
        No objectives defined — create a Content Plan or General Strategy for this client to see the comparison.
      </p>
    );
  }

  const detectedPillars = aiAnalysis?.contentPillars ?? [];
  const detectedAudience = aiAnalysis?.audienceProfile ?? null;
  const detectedPosting = aiAnalysis?.postingDays?.join(", ") ?? null;
  const detectedStrategy = aiAnalysis?.contentStrategySuggestion ?? null;

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <h3 className="text-sm font-semibold">Objective comparison</h3>
        <Badge variant="info" className="text-xs">Linked client</Badge>
      </div>

      <div className="rounded-md border border-border/40 bg-background/60 p-3 divide-y divide-border/20">
        {/* Audience */}
        <ComparisonRow
          label="Audience"
          detected={<TextOrDash text={detectedAudience} />}
          stated={<TextOrDash text={comparison.generalStrategyAudience} />}
        />

        {/* Content pillars */}
        <ComparisonRow
          label="Pillars"
          detected={<TagGroup items={detectedPillars} />}
          stated={<TagGroup items={comparison.generalStrategyPillars} />}
        />

        {/* Posting frequency */}
        <ComparisonRow
          label="Posting frequency"
          detected={<TextOrDash text={detectedPosting} />}
          stated={<TextOrDash text={comparison.generalStrategyPostingFrequency} />}
        />

        {/* Content strategy */}
        <ComparisonRow
          label="Content strategy"
          detected={<TextOrDash text={detectedStrategy} />}
          stated={<TextOrDash text={comparison.contentPlanStrategy} />}
        />

        {/* Objective */}
        {comparison.contentPlanObjective && (
          <ComparisonRow
            label="Plan objective"
            detected={<span className="text-xs text-muted-foreground/50">—</span>}
            stated={<TextOrDash text={comparison.contentPlanObjective} />}
          />
        )}

        {/* KPIs */}
        {comparison.contentPlanKpis.length > 0 && (
          <ComparisonRow
            label="Plan KPIs"
            detected={<TagGroup items={aiAnalysis?.recommendedKpis ?? []} />}
            stated={<TagGroup items={comparison.contentPlanKpis} />}
          />
        )}
      </div>
    </div>
  );
}
