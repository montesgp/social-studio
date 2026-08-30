import { test } from "node:test";
import assert from "node:assert/strict";
import {
  MAX_NICHE_HASHTAGS,
  NICHE_HASHTAG_MAX_LENGTH,
  addNicheHashtag,
  normalizeNicheHashtag,
  splitNicheHashtagInput,
  validateNicheHashtag,
} from "./niche-hashtags.ts";

// ── normalization mirrors the domain rule ────────────────────────────────────

test("normalizeNicheHashtag: strips the leading hash the operator naturally types", () => {
  assert.equal(normalizeNicheHashtag("#fitness"), "fitness");
});

test("normalizeNicheHashtag: lowercases so the discovery key is stable", () => {
  assert.equal(normalizeNicheHashtag("RealEstate"), "realestate");
});

test("normalizeNicheHashtag: trims surrounding whitespace, inside and outside the hash", () => {
  assert.equal(normalizeNicheHashtag("  # food  "), "food");
});

test("normalizeNicheHashtag: an empty or whitespace-only entry normalizes to nothing", () => {
  assert.equal(normalizeNicheHashtag("   "), "");
});

// ── validation mirrors the domain rule ───────────────────────────────────────

test("validateNicheHashtag: a plain lowercase word is accepted", () => {
  assert.equal(validateNicheHashtag("gastronomy"), null);
});

test("validateNicheHashtag: underscores are allowed", () => {
  assert.equal(validateNicheHashtag("real_estate"), null);
});

test("validateNicheHashtag: a single character is too short", () => {
  assert.match(validateNicheHashtag("a") ?? "", /between 2 and 64/);
});

test("validateNicheHashtag: past the maximum length is rejected", () => {
  const tooLong = "a".repeat(NICHE_HASHTAG_MAX_LENGTH + 1);
  assert.match(validateNicheHashtag(tooLong) ?? "", /between 2 and 64/);
});

test("validateNicheHashtag: spaces are rejected, because a hashtag has none", () => {
  assert.match(validateNicheHashtag("real estate") ?? "", /lowercase alphanumeric/);
});

test("validateNicheHashtag: punctuation is rejected", () => {
  assert.match(validateNicheHashtag("food!") ?? "", /lowercase alphanumeric/);
});

// ── adding to the curated list ───────────────────────────────────────────────

test("addNicheHashtag: appends a valid normalized hashtag", () => {
  const result = addNicheHashtag(["food"], "#Gastronomy");

  assert.equal(result.error, null);
  assert.deepEqual(result.hashtags, ["food", "gastronomy"]);
});

test("addNicheHashtag: a duplicate is rejected rather than silently doubled", () => {
  const result = addNicheHashtag(["food"], "FOOD");

  assert.match(result.error ?? "", /already/i);
  assert.deepEqual(result.hashtags, ["food"]);
});

test("addNicheHashtag: an invalid hashtag leaves the list untouched and explains why", () => {
  const result = addNicheHashtag(["food"], "not valid!");

  assert.match(result.error ?? "", /lowercase alphanumeric/);
  assert.deepEqual(result.hashtags, ["food"]);
});

test("addNicheHashtag: an empty entry is a no-op with no error to nag about", () => {
  const result = addNicheHashtag(["food"], "   ");

  assert.equal(result.error, null);
  assert.deepEqual(result.hashtags, ["food"]);
});

test("addNicheHashtag: the curated list stops at the platform maximum", () => {
  const full = Array.from({ length: MAX_NICHE_HASHTAGS }, (_, i) => `tag${i}`);
  const result = addNicheHashtag(full, "onemore");

  assert.match(result.error ?? "", /at most 20/);
  assert.equal(result.hashtags.length, MAX_NICHE_HASHTAGS);
});

// ── pasting several at once ──────────────────────────────────────────────────

test("splitNicheHashtagInput: splits on commas so a pasted list is usable", () => {
  assert.deepEqual(splitNicheHashtagInput("food, gastronomy,#chef"), ["food", "gastronomy", "#chef"]);
});

test("splitNicheHashtagInput: splits on whitespace too", () => {
  assert.deepEqual(splitNicheHashtagInput("food  gastronomy"), ["food", "gastronomy"]);
});

test("splitNicheHashtagInput: an empty input yields no entries", () => {
  assert.deepEqual(splitNicheHashtagInput("  ,  "), []);
});
