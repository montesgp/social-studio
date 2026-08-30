import { test } from "node:test";
import assert from "node:assert/strict";
import { computeCaptionLimits, CAPTION_MAX_LENGTH, CAPTION_MAX_HASHTAGS } from "./caption-limits.ts";

test("computeCaptionLimits: within both limits reports no violation", () => {
  const result = computeCaptionLimits("Hello world #brand");

  assert.equal(result.length, 18);
  assert.equal(result.hashtagCount, 1);
  assert.equal(result.isOverLength, false);
  assert.equal(result.isOverHashtags, false);
  assert.equal(result.isEmpty, false);
});

test("computeCaptionLimits: empty/whitespace-only is flagged empty", () => {
  const result = computeCaptionLimits("   ");

  assert.equal(result.isEmpty, true);
});

test("computeCaptionLimits: exceeding 2200 chars is flagged over length", () => {
  const result = computeCaptionLimits("a".repeat(CAPTION_MAX_LENGTH + 1));

  assert.equal(result.length, CAPTION_MAX_LENGTH + 1);
  assert.equal(result.isOverLength, true);
});

test("computeCaptionLimits: exactly at the length ceiling is NOT over", () => {
  const result = computeCaptionLimits("a".repeat(CAPTION_MAX_LENGTH));

  assert.equal(result.isOverLength, false);
});

test("computeCaptionLimits: exceeding 30 hashtags is flagged over hashtags", () => {
  const tooMany = Array.from({ length: CAPTION_MAX_HASHTAGS + 1 }, (_, i) => `#t${i}`).join(" ");
  const result = computeCaptionLimits(tooMany);

  assert.equal(result.hashtagCount, CAPTION_MAX_HASHTAGS + 1);
  assert.equal(result.isOverHashtags, true);
});

test("computeCaptionLimits: exactly 30 hashtags is NOT over", () => {
  const exact = Array.from({ length: CAPTION_MAX_HASHTAGS }, (_, i) => `#t${i}`).join(" ");
  const result = computeCaptionLimits(exact);

  assert.equal(result.hashtagCount, CAPTION_MAX_HASHTAGS);
  assert.equal(result.isOverHashtags, false);
});
