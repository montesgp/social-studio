"use client";

/**
 * ND-10 / design §11 — renders OpportunityScore and ConfidenceScore as two distinct labelled
 * values. `buildScorePair` (lib/social/niche-discovery/score-pair.ts) is the ONLY decision this
 * component defers to; there is no code path here that averages or otherwise combines the two.
 */

import { buildScorePair } from "@/lib/social/niche-discovery/score-pair";

interface ScorePairBadgeProps {
  opportunityScore: number;
  confidenceScore: number;
}

export function ScorePairBadge({ opportunityScore, confidenceScore }: ScorePairBadgeProps) {
  const [opportunity, confidence] = buildScorePair(opportunityScore, confidenceScore);

  return (
    <div className="flex items-center gap-2" data-testid="score-pair-badge">
      <span className="rounded-full border border-primary/40 bg-primary/10 px-2.5 py-1 text-xs font-semibold text-primary">
        {opportunity.label}: {opportunity.value}
      </span>
      <span className="rounded-full border border-border bg-background/40 px-2.5 py-1 text-xs font-semibold text-muted-foreground">
        {confidence.label}: {confidence.value}
      </span>
    </div>
  );
}
