import { test } from "node:test";
import assert from "node:assert/strict";

import {
  describeAnalysisPeriod,
  describeDataFreshness,
  describeOpportunityName,
  deriveRisks,
} from "./opportunity-narrative";

// ND-13 — `NicheOpportunityRecord`/`DiscoveryRunStatusResponse` (design §9/§10) carry no literal
// "name", "description", "analysis period", "data freshness" or "risks/limitations" field. These
// are AUTHORED derivations over the persisted fields the DTOs already carry (flagged deviation,
// same discipline as prior work units' authored decisions): a name from the content pattern and
// sub-niche label, a period from the run's window, freshness from the observation timestamp, and
// risk bullets from degraded-run signals the operator already has evidence for.

test("describeOpportunityName prefers the sub-niche label when present", () => {
  assert.equal(describeOpportunityName("TalkingHead", "budget travel hacks"), "budget travel hacks");
});

test("describeOpportunityName falls back to the content pattern when no sub-niche label exists", () => {
  assert.equal(describeOpportunityName("TalkingHead", null), "TalkingHead");
});

test("describeAnalysisPeriod renders the run window in days", () => {
  assert.equal(describeAnalysisPeriod(90), "Last 90 days");
});

test("describeDataFreshness reports same-day observation", () => {
  const observed = new Date("2026-08-14T08:00:00Z");
  const now = new Date("2026-08-14T12:00:00Z");
  assert.equal(describeDataFreshness(observed, now), "Observed today");
});

test("describeDataFreshness reports elapsed days for an older observation", () => {
  const observed = new Date("2026-08-10T08:00:00Z");
  const now = new Date("2026-08-14T08:00:00Z");
  assert.equal(describeDataFreshness(observed, now), "Observed 4 days ago");
});

test("deriveRisks flags a degraded run (semantic fallback or partial sourcing)", () => {
  const risks = deriveRisks({
    semanticDegraded: true,
    unconfiguredSourceKeys: [],
    failedSourceKeys: [],
    channelSampleSize: 20,
    videoSampleSize: 40,
    excludedSignalKeys: [],
  });
  assert.equal(
    risks.includes("Semantic classification fell back to lexical clustering for part of this run."),
    true
  );
});

test("deriveRisks flags a thin sample size", () => {
  const risks = deriveRisks({
    semanticDegraded: false,
    unconfiguredSourceKeys: [],
    failedSourceKeys: [],
    channelSampleSize: 2,
    videoSampleSize: 3,
    excludedSignalKeys: [],
  });
  assert.equal(risks.includes("Small evidence sample — confidence should be read alongside this."), true);
});

test("deriveRisks flags an excluded signal", () => {
  const risks = deriveRisks({
    semanticDegraded: false,
    unconfiguredSourceKeys: [],
    failedSourceKeys: [],
    channelSampleSize: 20,
    videoSampleSize: 40,
    excludedSignalKeys: ["CrossPlatformValidation"],
  });
  assert.equal(
    risks.includes("CrossPlatformValidation could not be scored and was excluded — weights were renormalized."),
    true
  );
});

test("deriveRisks returns an empty list for a clean, well-sampled run", () => {
  const risks = deriveRisks({
    semanticDegraded: false,
    unconfiguredSourceKeys: [],
    failedSourceKeys: [],
    channelSampleSize: 40,
    videoSampleSize: 80,
    excludedSignalKeys: [],
  });
  assert.deepEqual(risks, []);
});
