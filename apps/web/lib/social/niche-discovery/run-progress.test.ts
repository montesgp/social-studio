import { test } from "node:test";
import assert from "node:assert/strict";

import { STALLED_AFTER_MS, describeRunProgress } from "./run-progress";

// A discovery run is a background job the operator cannot see into. While it works the screen must say
// WHAT it is doing, and — the lesson from run e6124e47, which sat in Running forever after the worker
// threw — it must stop claiming progress once the run has plainly stopped moving.

function progress(status: string, elapsedMs = 0, providerUnitsSpent = 0) {
  return describeRunProgress({ status, elapsedMs, providerUnitsSpent });
}

test("a requested run reads as queued, not as working", () => {
  const p = progress("Requested");
  assert.equal(p.isBlocking, true);
  assert.match(p.description, /queue/i);
});

test("a running run names the phase it is in", () => {
  assert.match(progress("Running").description, /harvest|sourc/i);
});

test("once the provider has been billed, the phase advances past sourcing", () => {
  const early = progress("Running", 1000, 0);
  const later = progress("Running", 1000, 120);

  assert.notEqual(early.description, later.description);
});

test("terminal states never block the screen", () => {
  for (const status of ["Completed", "PartiallyCompleted", "Failed"]) {
    assert.equal(progress(status).isBlocking, false, `${status} must not block`);
  }
});

// The bug this guards: a crashed worker left the run Running, so the UI polled forever and the operator
// read a crash as a slow job.
test("a run stuck far past the stall threshold is reported as stalled", () => {
  const p = progress("Running", STALLED_AFTER_MS + 1);

  assert.equal(p.isStalled, true);
  assert.match(`${p.title} ${p.description}`, /longer than expected|stalled|stopped/i);
});

test("a run within the stall threshold is not reported as stalled", () => {
  assert.equal(progress("Running", STALLED_AFTER_MS - 1).isStalled, false);
});

test("progress never reaches completion while the run is still working", () => {
  assert.ok(progress("Running", 60_000, 400).percent < 100);
});

test("progress advances as the run proceeds", () => {
  assert.ok(progress("Running", 30_000, 200).percent > progress("Requested").percent);
});
