/**
 * AUTHORED addition beyond design §11's literal file list (flagged in the WU-12 commit): the
 * detail view's "name/description", "analysis period", "data freshness", and "risks/limitations"
 * fields required by ND-13 have no dedicated column on `NicheOpportunityRecord` /
 * `DiscoveryRunStatusResponse` (design §9/§10). Rather than leaving those ND-13 fields blank, this
 * module derives operator-facing text from the persisted fields the DTOs already carry. Pure,
 * unit-tested, no I/O — matches the house pattern of keeping decision logic out of components.
 */

export function describeOpportunityName(contentPattern: string, subNicheLabel: string | null): string {
  return subNicheLabel?.trim() ? subNicheLabel : contentPattern;
}

export function describeAnalysisPeriod(windowDays: number): string {
  return `Last ${windowDays} days`;
}

const MILLISECONDS_PER_DAY = 24 * 60 * 60 * 1000;

export function describeDataFreshness(observedAtUtc: Date, nowUtc: Date): string {
  const elapsedDays = Math.floor((nowUtc.getTime() - observedAtUtc.getTime()) / MILLISECONDS_PER_DAY);
  if (elapsedDays <= 0) {
    return "Observed today";
  }
  return `Observed ${elapsedDays} day${elapsedDays === 1 ? "" : "s"} ago`;
}

export interface RiskInputs {
  semanticDegraded: boolean;
  unconfiguredSourceKeys: readonly string[];
  failedSourceKeys: readonly string[];
  channelSampleSize: number;
  videoSampleSize: number;
  excludedSignalKeys: readonly string[];
}

/** A sample under this many channels or videos is called out as thin evidence. */
const THIN_SAMPLE_CHANNEL_FLOOR = 3;
const THIN_SAMPLE_VIDEO_FLOOR = 5;

export function deriveRisks(inputs: RiskInputs): string[] {
  const risks: string[] = [];

  if (inputs.semanticDegraded) {
    risks.push("Semantic classification fell back to lexical clustering for part of this run.");
  }

  if (inputs.unconfiguredSourceKeys.length > 0) {
    risks.push(`Some origins were unconfigured for this run: ${inputs.unconfiguredSourceKeys.join(", ")}.`);
  }

  if (inputs.failedSourceKeys.length > 0) {
    risks.push(`Some origins failed during this run: ${inputs.failedSourceKeys.join(", ")}.`);
  }

  if (inputs.channelSampleSize < THIN_SAMPLE_CHANNEL_FLOOR || inputs.videoSampleSize < THIN_SAMPLE_VIDEO_FLOOR) {
    risks.push("Small evidence sample — confidence should be read alongside this.");
  }

  for (const key of inputs.excludedSignalKeys) {
    risks.push(`${key} could not be scored and was excluded — weights were renormalized.`);
  }

  return risks;
}
