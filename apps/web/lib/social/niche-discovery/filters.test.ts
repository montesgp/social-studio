import { test } from "node:test";
import assert from "node:assert/strict";

import { applyFilters, type FilterableOpportunity, type OpportunityFilters } from "./filters";
import { sortByAxis } from "./ranking";

function opportunity(overrides: Partial<FilterableOpportunity> = {}): FilterableOpportunity {
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

const NO_FILTERS: OpportunityFilters = {
  marketCode: null,
  contentFormat: null,
  hasTrend: null,
  competitionBand: null,
};

test("no filters returns every opportunity unchanged", () => {
  const items = [opportunity({ id: "a" }), opportunity({ id: "b" })];
  assert.deepEqual(applyFilters(items, NO_FILTERS).map((i) => i.id), ["a", "b"]);
});

test("filters by market code", () => {
  const items = [opportunity({ id: "mkt-one-id", marketCode: "mkt-one" }), opportunity({ id: "mkt-two-id", marketCode: "mkt-two" })];
  const filtered = applyFilters(items, { ...NO_FILTERS, marketCode: "mkt-two" });
  assert.deepEqual(filtered.map((i) => i.id), ["mkt-two-id"]);
});

test("filters by content format", () => {
  const items = [opportunity({ id: "video", contentFormat: "video" }), opportunity({ id: "short", contentFormat: "short" })];
  const filtered = applyFilters(items, { ...NO_FILTERS, contentFormat: "short" });
  assert.deepEqual(filtered.map((i) => i.id), ["short"]);
});

test("filters to only opportunities with a reported trend value", () => {
  const items = [opportunity({ id: "known", growthProxy: 0.4 }), opportunity({ id: "unknown", growthProxy: null })];
  const filtered = applyFilters(items, { ...NO_FILTERS, hasTrend: true });
  assert.deepEqual(filtered.map((i) => i.id), ["known"]);
});

test("filters by competition band low/medium/high", () => {
  const items = [
    opportunity({ id: "low", targetMarketCompetition: 0.1 }),
    opportunity({ id: "mid", targetMarketCompetition: 0.5 }),
    opportunity({ id: "high", targetMarketCompetition: 0.9 }),
    opportunity({ id: "unknown", targetMarketCompetition: null }),
  ];
  assert.deepEqual(applyFilters(items, { ...NO_FILTERS, competitionBand: "low" }).map((i) => i.id), ["low"]);
  assert.deepEqual(applyFilters(items, { ...NO_FILTERS, competitionBand: "medium" }).map((i) => i.id), ["mid"]);
  assert.deepEqual(applyFilters(items, { ...NO_FILTERS, competitionBand: "high" }).map((i) => i.id), ["high"]);
});

// ND-12 scenario: filter to one target market and sort by opportunity score descending.
test("filter-by-market then sort-by-opportunity-score integration scenario", () => {
  const items = [
    opportunity({ id: "mkt-one-low", marketCode: "mkt-one", opportunityScore: 20 }),
    opportunity({ id: "mkt-one-high", marketCode: "mkt-one", opportunityScore: 90 }),
    opportunity({ id: "mkt-two-high", marketCode: "mkt-two", opportunityScore: 99 }),
  ];
  const filtered = applyFilters(items, { ...NO_FILTERS, marketCode: "mkt-one" });
  const sorted = sortByAxis(filtered, "opportunityScore", "desc");
  assert.deepEqual(sorted.map((i) => i.id), ["mkt-one-high", "mkt-one-low"]);
});
