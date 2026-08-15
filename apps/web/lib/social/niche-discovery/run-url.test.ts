import { test } from "node:test";
import assert from "node:assert/strict";

import { buildRunPath, readRunIdFromSearchParams } from "./run-url";

// WU-14 / design §10 — there is no cross-run listing endpoint by design, so the URL is the only
// handle the operator has to a run they already paid API quota for. Losing the id on reload means
// losing access to the run entirely, so this round-trip is tested on its own.

test("buildRunPath puts the run id in the query string", () => {
  assert.equal(buildRunPath("abc-123"), "/niche-discovery?run=abc-123");
});

test("buildRunPath with no run id points at the bare route", () => {
  assert.equal(buildRunPath(null), "/niche-discovery");
});

test("readRunIdFromSearchParams reads a run id from URLSearchParams", () => {
  const params = new URLSearchParams("run=abc-123&other=1");
  assert.equal(readRunIdFromSearchParams(params), "abc-123");
});

test("readRunIdFromSearchParams returns null when absent, blank, or malformed", () => {
  assert.equal(readRunIdFromSearchParams(new URLSearchParams("")), null);
  assert.equal(readRunIdFromSearchParams(new URLSearchParams("run=")), null);
  assert.equal(readRunIdFromSearchParams(null), null);
});

test("readRunIdFromSearchParams accepts a plain string-keyed record (server searchParams)", () => {
  assert.equal(readRunIdFromSearchParams({ run: "abc-123" }), "abc-123");
  assert.equal(readRunIdFromSearchParams({ run: ["abc-123", "def-456"] }), "abc-123");
  assert.equal(readRunIdFromSearchParams({}), null);
});
