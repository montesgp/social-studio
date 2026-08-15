/**
 * Pure predicate builders for the viability dashboard (ND-12, design §11).
 */

import type { RankableOpportunity } from "./ranking";

export type FilterableOpportunity = RankableOpportunity;

export type CompetitionBand = "low" | "medium" | "high";

export interface OpportunityFilters {
  marketCode: string | null;
  contentFormat: string | null;
  /** `true` = only opportunities with a reported trend value; `null` = no trend filter. */
  hasTrend: boolean | null;
  competitionBand: CompetitionBand | null;
}

/** Competition bands: low < 1/3, medium in [1/3, 2/3), high >= 2/3. An unavailable value matches none. */
function matchesCompetitionBand(value: number | null, band: CompetitionBand): boolean {
  if (value === null) {
    return false;
  }
  if (band === "low") return value < 1 / 3;
  if (band === "medium") return value >= 1 / 3 && value < 2 / 3;
  return value >= 2 / 3;
}

export function applyFilters<T extends FilterableOpportunity>(
  items: readonly T[],
  filters: OpportunityFilters
): T[] {
  return items.filter((item) => {
    if (filters.marketCode && item.marketCode !== filters.marketCode) return false;
    if (filters.contentFormat && item.contentFormat !== filters.contentFormat) return false;
    if (filters.hasTrend === true && item.growthProxy === null) return false;
    if (filters.competitionBand && !matchesCompetitionBand(item.targetMarketCompetition, filters.competitionBand)) {
      return false;
    }
    return true;
  });
}
