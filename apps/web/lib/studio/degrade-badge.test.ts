import { test } from "node:test";
import assert from "node:assert/strict";
import { getDegradeReason } from "./degrade-badge.ts";

test("getDegradeReason: returns the token when the envelope carries a non-empty degradeReason", () => {
  assert.equal(getDegradeReason({ markup: "<div></div>", degradeReason: "photo-image-unavailable" }), "photo-image-unavailable");
});

test("getDegradeReason: returns null when degradeReason is absent (regression — no badge)", () => {
  assert.equal(getDegradeReason({ markup: "<div></div>" }), null);
});

test("getDegradeReason: returns null when degradeReason is explicitly null", () => {
  assert.equal(getDegradeReason({ markup: "<div></div>", degradeReason: null }), null);
});

test("getDegradeReason: returns null when degradeReason is an empty string", () => {
  assert.equal(getDegradeReason({ markup: "<div></div>", degradeReason: "" }), null);
});

test("getDegradeReason: returns null for a null/undefined envelope", () => {
  assert.equal(getDegradeReason(null), null);
  assert.equal(getDegradeReason(undefined), null);
});

test("getDegradeReason: survives a JSON round-trip (edit + autosave shape, CAFF-CS-02)", () => {
  const envelope = { markup: "<div></div>", degradeReason: "designer-unusable" };
  const roundTripped = JSON.parse(JSON.stringify(envelope)) as typeof envelope;
  assert.equal(getDegradeReason(roundTripped), "designer-unusable");
});
