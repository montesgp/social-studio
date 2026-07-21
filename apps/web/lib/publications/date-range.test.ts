import { test } from "node:test";
import assert from "node:assert/strict";
import { lastSevenDaysRange } from "./date-range.ts";

test("lastSevenDaysRange: toUtc equals the reference instant", () => {
  const now = new Date("2026-07-20T12:00:00.000Z");
  const range = lastSevenDaysRange(now);

  assert.equal(range.toUtc, "2026-07-20T12:00:00.000Z");
});

test("lastSevenDaysRange: fromUtc is exactly 7 days before toUtc", () => {
  const now = new Date("2026-07-20T12:00:00.000Z");
  const range = lastSevenDaysRange(now);

  assert.equal(range.fromUtc, "2026-07-13T12:00:00.000Z");
});

test("lastSevenDaysRange: defaults to the current instant when no reference is given", () => {
  const before = Date.now();
  const range = lastSevenDaysRange();
  const after = Date.now();

  const toMs = new Date(range.toUtc).getTime();
  assert.ok(toMs >= before && toMs <= after);
});
