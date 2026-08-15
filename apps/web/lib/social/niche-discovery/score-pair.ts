/**
 * ND-10 / design §11 — the single most important UI invariant in this change: OpportunityScore
 * and ConfidenceScore are ALWAYS shown together and NEVER averaged into one number. This is the
 * only function `score-pair-badge.tsx` calls to decide what to render — there is no other code
 * path that could combine the two scores.
 */

export interface ScorePairEntry {
  label: "Opportunity" | "Confidence";
  value: number;
}

export function buildScorePair(opportunityScore: number, confidenceScore: number): [ScorePairEntry, ScorePairEntry] {
  return [
    { label: "Opportunity", value: opportunityScore },
    { label: "Confidence", value: confidenceScore },
  ];
}
