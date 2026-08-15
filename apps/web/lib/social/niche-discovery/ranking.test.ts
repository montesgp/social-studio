import { test } from "node:test";
import assert from "node:assert/strict";

import { RANKING_AXES, compareByAxis, sortByAxis, type RankableOpportunity } from "./ranking";

// ND-12 — the dashboard MUST sort by opportunity score, confidence score, market, format, trend,
// competition, and automation suitability. Trend is represented here by GrowthProxy.

function opportunity(overrides: Partial<RankableOpportunity> = {}): RankableOpportunity {
  return {
    id: "a",
    opportunityScore: 50,
    confidenceScore: 50,
    marketCode: "mkt-one",
    contentFormat: "video",
    growthProxy: null,
    targetMarketCompetition: null,
    automationSuitability: null,
    ...overrides,
  };
}

test("RANKING_AXES exposes exactly the seven required sort dimensions", () => {
  assert.deepEqual(
    [...RANKING_AXES].sort(),
    [
      "automationSuitability",
      "confidenceScore",
      "contentFormat",
      "marketCode",
      "opportunityScore",
      "targetMarketCompetition",
      "trend",
    ].sort()
  );
});

test("sorts by opportunityScore descending", () => {
  const items = [opportunity({ id: "low", opportunityScore: 10 }), opportunity({ id: "high", opportunityScore: 90 })];
  const sorted = sortByAxis(items, "opportunityScore", "desc");
  assert.deepEqual(sorted.map((item) => item.id), ["high", "low"]);
});

test("sorts by confidenceScore ascending", () => {
  const items = [opportunity({ id: "high", confidenceScore: 90 }), opportunity({ id: "low", confidenceScore: 10 })];
  const sorted = sortByAxis(items, "confidenceScore", "asc");
  assert.deepEqual(sorted.map((item) => item.id), ["low", "high"]);
});

test("sorts by marketCode alphabetically", () => {
  const items = [opportunity({ id: "b", marketCode: "mkt-two" }), opportunity({ id: "a", marketCode: "mkt-one" })];
  const sorted = sortByAxis(items, "marketCode", "asc");
  assert.deepEqual(sorted.map((item) => item.id), ["a", "b"]);
});

test("sorts by contentFormat alphabetically", () => {
  const items = [opportunity({ id: "video", contentFormat: "video" }), opportunity({ id: "short", contentFormat: "short" })];
  const sorted = sortByAxis(items, "contentFormat", "asc");
  assert.deepEqual(sorted.map((item) => item.id), ["short", "video"]);
});

test("sorts by trend (growthProxy) with unavailable values ranked last regardless of direction", () => {
  const items = [
    opportunity({ id: "none", growthProxy: null }),
    opportunity({ id: "low", growthProxy: 0.2 }),
    opportunity({ id: "high", growthProxy: 0.8 }),
  ];
  assert.deepEqual(sortByAxis(items, "trend", "desc").map((i) => i.id), ["high", "low", "none"]);
  assert.deepEqual(sortByAxis(items, "trend", "asc").map((i) => i.id), ["low", "high", "none"]);
});

test("sorts by targetMarketCompetition with unavailable values ranked last", () => {
  const items = [
    opportunity({ id: "none", targetMarketCompetition: null }),
    opportunity({ id: "low", targetMarketCompetition: 0.1 }),
    opportunity({ id: "high", targetMarketCompetition: 0.9 }),
  ];
  assert.deepEqual(sortByAxis(items, "targetMarketCompetition", "asc").map((i) => i.id), ["low", "high", "none"]);
});

test("sorts by automationSuitability with unavailable values ranked last", () => {
  const items = [
    opportunity({ id: "none", automationSuitability: null }),
    opportunity({ id: "low", automationSuitability: 0.25 }),
    opportunity({ id: "high", automationSuitability: 0.85 }),
  ];
  assert.deepEqual(sortByAxis(items, "automationSuitability", "desc").map((i) => i.id), ["high", "low", "none"]);
});

test("compareByAxis never combines opportunity and confidence into one comparison key", () => {
  const higherOpportunityLowerConfidence = opportunity({ id: "a", opportunityScore: 94, confidenceScore: 37 });
  const lowerOpportunityHigherConfidence = opportunity({ id: "b", opportunityScore: 88, confidenceScore: 95 });
  assert.equal(
    compareByAxis(higherOpportunityLowerConfidence, lowerOpportunityHigherConfidence, "opportunityScore", "desc") < 0,
    true
  );
  assert.equal(
    compareByAxis(higherOpportunityLowerConfidence, lowerOpportunityHigherConfidence, "confidenceScore", "desc") > 0,
    true
  );
});
