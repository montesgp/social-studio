import { test } from "node:test";
import assert from "node:assert/strict";
import { shouldApplyBackendSync } from "./hydrate-guard.ts";

test("shouldApplyBackendSync: applies the very first backend sync of the session", () => {
  assert.equal(shouldApplyBackendSync(false), true);
});

test("shouldApplyBackendSync: does NOT apply a later resync (e.g. token refresh) once hydrated", () => {
  // Regression: a background resync triggered by a token refresh must never clobber a just-made
  // local theme/date-format selection with a stale GET response (race condition).
  assert.equal(shouldApplyBackendSync(true), false);
});
