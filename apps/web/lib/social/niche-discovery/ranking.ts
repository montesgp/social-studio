/**
 * Pure sort comparators for the viability dashboard (ND-12, design §11).
 *
 * The dashboard sorts across seven axes: opportunity score, confidence score, market, format,
 * trend, competition, and automation suitability. `opportunityScore` and `confidenceScore` are
 * ALWAYS two separate axes here — there is no combined-score axis, matching ND-10's never-averaged
 * display rule.
 */

export type RankingAxis =
  | "opportunityScore"
  | "confidenceScore"
  | "marketCode"
  | "contentFormat"
  | "trend"
  | "targetMarketCompetition"
  | "automationSuitability";

export const RANKING_AXES: readonly RankingAxis[] = [
  "opportunityScore",
  "confidenceScore",
  "marketCode",
  "contentFormat",
  "trend",
  "targetMarketCompetition",
  "automationSuitability",
];

export type SortDirection = "asc" | "desc";

/** The subset of `NicheOpportunitySummary` fields ranking actually depends on. */
export interface RankableOpportunity {
  id: string;
  opportunityScore: number;
  confidenceScore: number;
  marketCode: string;
  contentFormat: string;
  /** Growth/velocity proxy — the "trend" axis. Nullable, never zero-filled (ND-05). */
  growthProxy: number | null;
  targetMarketCompetition: number | null;
  automationSuitability: number | null;
}

const NUMERIC_AXES: readonly RankingAxis[] = [
  "opportunityScore",
  "confidenceScore",
  "trend",
  "targetMarketCompetition",
  "automationSuitability",
];

function numericValue(item: RankableOpportunity, axis: RankingAxis): number | null {
  switch (axis) {
    case "opportunityScore":
      return item.opportunityScore;
    case "confidenceScore":
      return item.confidenceScore;
    case "trend":
      return item.growthProxy;
    case "targetMarketCompetition":
      return item.targetMarketCompetition;
    case "automationSuitability":
      return item.automationSuitability;
    default:
      return null;
  }
}

function stringValue(item: RankableOpportunity, axis: RankingAxis): string {
  return axis === "marketCode" ? item.marketCode : item.contentFormat;
}

/**
 * Compares two opportunities on exactly one axis. An unavailable numeric value (ND-05) always
 * sorts last, regardless of direction — "unavailable" is not "low" or "high", it is unranked.
 */
export function compareByAxis(
  a: RankableOpportunity,
  b: RankableOpportunity,
  axis: RankingAxis,
  direction: SortDirection
): number {
  const sign = direction === "asc" ? 1 : -1;

  if (NUMERIC_AXES.includes(axis)) {
    const av = numericValue(a, axis);
    const bv = numericValue(b, axis);
    if (av === null && bv === null) return 0;
    if (av === null) return 1;
    if (bv === null) return -1;
    return (av - bv) * sign;
  }

  return stringValue(a, axis).localeCompare(stringValue(b, axis)) * sign;
}

export function sortByAxis<T extends RankableOpportunity>(
  items: readonly T[],
  axis: RankingAxis,
  direction: SortDirection
): T[] {
  return [...items].sort((a, b) => compareByAxis(a, b, axis, direction));
}
