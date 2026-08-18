import { test } from "node:test";
import assert from "node:assert/strict";

import { describeHistoryEntry } from "./run-history";
import type { DiscoveryRunHistoryEntry } from "@/lib/api/niche-discovery";

// The history exists so an expensive result survives closing the tool. Each row has to answer "which
// run was this, and is it worth reopening" without loading the hundreds of rows behind it.

function entry(overrides: Partial<DiscoveryRunHistoryEntry> = {}): DiscoveryRunHistoryEntry {
  return {
    runId: "run-1",
    status: "Completed",
    sourceMarketCode: "es-ZZ",
    targetMarketCode: "pt-ZZ",
    contentFormat: "video",
    windowDays: 90,
    depth: "Standard",
    seedTerms: [],
    semanticDegraded: false,
    failureReasonKey: null,
    providerUnitsSpent: 20,
    createdAtUtc: "2026-08-15T10:22:51Z",
    completedAtUtc: "2026-08-15T10:23:10Z",
    ...overrides,
  };
}

test("an open discovery run is labelled as such, not as an empty search", () => {
  const described = describeHistoryEntry(entry({ seedTerms: [] }));
  assert.match(described.intent, /discover/i);
});

test("a seeded run shows the terms it was asked about", () => {
  const described = describeHistoryEntry(entry({ seedTerms: ["budget travel", "street food"] }));

  assert.match(described.intent, /budget travel/);
  assert.match(described.intent, /street food/);
});

test("a long seed term list is truncated rather than pushed off the row", () => {
  const described = describeHistoryEntry(
    entry({ seedTerms: ["one", "two", "three", "four", "five", "six"] })
  );

  assert.ok(described.intent.length < 120, `intent too long for a list row: ${described.intent}`);
  assert.match(described.intent, /\+\d+ more|…/);
});

test("only runs with results are openable", () => {
  assert.equal(describeHistoryEntry(entry({ status: "Completed" })).isOpenable, true);
  assert.equal(describeHistoryEntry(entry({ status: "PartiallyCompleted" })).isOpenable, true);
  assert.equal(describeHistoryEntry(entry({ status: "Failed" })).isOpenable, false);
  assert.equal(describeHistoryEntry(entry({ status: "Running" })).isOpenable, false);
});

// A degraded run still ranked, but its niche NAMES came from the lexical fallback rather than the
// semantic layer. Reopening it without knowing that is how a caveat gets lost.
test("a degraded run says so", () => {
  const described = describeHistoryEntry(entry({ status: "PartiallyCompleted", semanticDegraded: true }));

  assert.ok(described.warning);
  assert.match(described.warning!, /degrad|fallback|partial/i);
});

test("a clean run carries no warning", () => {
  assert.equal(describeHistoryEntry(entry()).warning, null);
});

test("a failed run surfaces its reason instead of a bare status", () => {
  const described = describeHistoryEntry(
    entry({ status: "Failed", failureReasonKey: "social.niche_discovery.origin_unconfigured", completedAtUtc: null })
  );

  assert.ok(described.warning);
  assert.match(described.warning!, /origin_unconfigured/);
});
