import { test } from "node:test";
import assert from "node:assert/strict";
import { formatUserDate, formatUserTime, parseUserDate, datePatternHint } from "./date.ts";
import { formatLabel } from "./format-key.ts";

test("parseUserDate reads the user's pattern, not the browser's", () => {
  // The same text means different dates under different patterns — that is the whole problem a
  // native <input type="date"> could not express.
  assert.equal(parseUserDate("06/08/2026", "dd/MM/yyyy"), "2026-08-06");
  assert.equal(parseUserDate("06/08/2026", "MM/dd/yyyy"), "2026-06-08");
  assert.equal(parseUserDate("2026-08-06", "yyyy-MM-dd"), "2026-08-06");
});

test("parseUserDate accepts any separator the user types", () => {
  assert.equal(parseUserDate("6-8-2026", "dd/MM/yyyy"), "2026-08-06");
  assert.equal(parseUserDate("6.8.2026", "dd/MM/yyyy"), "2026-08-06");
});

test("parseUserDate rejects impossible and incomplete dates", () => {
  assert.equal(parseUserDate("31/02/2026", "dd/MM/yyyy"), null); // February has no 31st
  assert.equal(parseUserDate("13/13/2026", "dd/MM/yyyy"), null);
  assert.equal(parseUserDate("06/08", "dd/MM/yyyy"), null);
  assert.equal(parseUserDate("06/08/26", "dd/MM/yyyy"), null); // two-digit year is ambiguous
  assert.equal(parseUserDate("", "dd/MM/yyyy"), null);
});

test("parseUserDate round-trips through formatUserDate", () => {
  for (const pattern of ["dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"] as const) {
    const iso = "2026-08-06";
    const rendered = formatUserDate(iso, pattern);
    assert.equal(parseUserDate(rendered, pattern), iso, `round trip failed for ${pattern}`);
  }
});

test("datePatternHint mirrors the active pattern", () => {
  assert.equal(datePatternHint("dd/MM/yyyy"), "dd/mm/yyyy");
  assert.equal(datePatternHint("yyyy-MM-dd"), "yyyy-mm-dd");
});

test("formatUserTime renders 24h regardless of locale", () => {
  const iso = new Date(2026, 7, 6, 15, 4).toISOString();
  assert.equal(formatUserTime(iso), "15:04");
});

test("formatLabel humanizes a wire format key", () => {
  assert.equal(formatLabel("reel-script"), "Reel Script");
  assert.equal(formatLabel("feed-carousel"), "Feed Carousel");
  assert.equal(formatLabel("feed_single"), "Feed Single");
  assert.equal(formatLabel(""), "");
});
