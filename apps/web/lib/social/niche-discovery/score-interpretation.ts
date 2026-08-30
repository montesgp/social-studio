/**
 * How to READ an OpportunityScore (ND-09, design §7.1).
 *
 * Every signal feeding the score is normalized min-max across the clusters of ONE RUN
 * (`AvailabilityWeightedAggregate.NormalizeWithinSet`). That makes the score a RANKING INSIDE THE RUN,
 * not an absolute probability that a niche will succeed: 0.9 means "the strongest thing this run
 * found", and if every candidate was weak, the least weak one still lands near 1.0.
 *
 * Saying so in the UI is not decoration. A relative score read as a probability is exactly how an
 * operator ends up building a channel for a market that has no audience at all.
 */

/**
 * What normalization returns when there is nothing to compare against: a single value in the set, or
 * a set where every value is identical, collapses to this — and so does a score whose every signal is
 * unavailable. It reads like "average" and means "not comparable".
 */
export const NOT_COMPARABLE_VALUE = 0.5;

export interface ScoreInterpretation {
  headline: string;
  caveat: string | null;
}

export function interpretRunScores(opportunityCount: number): ScoreInterpretation {
  const headline =
    "Scores rank opportunities within this run — they are not absolute probabilities of success. " +
    "A high score means the strongest candidate this run found, not a market proven to be viable.";

  if (opportunityCount > 1) {
    return { headline, caveat: null };
  }

  return {
    headline,
    caveat:
      `This run has ${opportunityCount === 1 ? "a single opportunity" : "no opportunities"}, so nothing ` +
      `can be ranked against anything: normalized signals fall back to ${NOT_COMPARABLE_VALUE}, which means ` +
      "not comparable — not average. Widen the analysis window or the sourcing before reading these numbers.",
  };
}
