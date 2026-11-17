import { test } from "node:test";
import assert from "node:assert/strict";
import { formatMs, median, percentile, summarizeEndpoints, summarizeRuns, totalBlockingTime } from "./stats.mjs";

test("percentile uses nearest rank and ignores non-finite values", () => {
  assert.equal(percentile([5, 1, 3, 2, 4], 50), 3);
  assert.equal(percentile([5, 1, 3, 2, 4], 95), 5);
  assert.equal(percentile([5, 1, 3, 2, 4], 0), 1);
  assert.equal(percentile([5, 1, 3, 2, 4], 100), 5);
  assert.equal(percentile([10, Number.NaN, null, 20], 50), 10);
  assert.equal(percentile([], 50), null);
  assert.equal(median([4, 2]), 2);
});

test("totalBlockingTime counts only the time above 50 ms per long task", () => {
  assert.equal(totalBlockingTime([40, 50, 120, 300]), 70 + 250);
  assert.equal(totalBlockingTime([]), 0);
});

test("summarizeRuns separates the cold first run from the warm median", () => {
  const runs = [
    { ttfb: 4000, ready: 9000 },
    { ttfb: 200, ready: 900 },
    { ttfb: 300, ready: 700 },
    { ttfb: 100, ready: 800 }
  ];
  assert.deepEqual(summarizeRuns(runs, ["ttfb", "ready"]), {
    cold: { ttfb: 4000, ready: 9000 },
    warm: { ttfb: 200, ready: 800 }
  });
  assert.deepEqual(summarizeRuns([{ ttfb: 50 }], ["ttfb"]), { cold: { ttfb: 50 }, warm: { ttfb: null } });
});

test("summarizeEndpoints groups by method and path, slowest median first", () => {
  const rows = summarizeEndpoints([
    { method: "GET", path: "/api/me", ms: 10 },
    { method: "GET", path: "/api/me", ms: 30 },
    { method: "GET", path: "/api/clients", ms: 200 },
    { method: "POST", path: "/api/me", ms: 5 }
  ]);
  assert.deepEqual(rows, [
    { endpoint: "GET /api/clients", count: 1, p50: 200, p95: 200, max: 200 },
    { endpoint: "GET /api/me", count: 2, p50: 10, p95: 30, max: 30 },
    { endpoint: "POST /api/me", count: 1, p50: 5, p95: 5, max: 5 }
  ]);
});

test("formatMs rounds and renders missing values as a dash", () => {
  assert.equal(formatMs(12.6), "13");
  assert.equal(formatMs(null), "-");
  assert.equal(formatMs(undefined), "-");
});
