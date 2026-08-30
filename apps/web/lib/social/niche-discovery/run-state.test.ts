import { test } from "node:test";
import assert from "node:assert/strict";

import { describeRunState, isRunDegraded, isRunPollable, isRunViewable } from "./run-state";

// WU-14 — mounting the viability dashboard for real means the operator must be able to tell
// Requested/Running/Completed/PartiallyCompleted/Failed apart at a glance, and a PartiallyCompleted
// run must never look like a full result (ND-09/design §10). This module is the single source of
// truth for that interpretation; the route/client components only render what it returns.

test("Requested and Running are pollable and not yet viewable", () => {
  assert.equal(isRunPollable("Requested"), true);
  assert.equal(isRunPollable("Running"), true);
  assert.equal(isRunViewable("Requested"), false);
  assert.equal(isRunViewable("Running"), false);
});

test("Completed and PartiallyCompleted are viewable and no longer pollable", () => {
  assert.equal(isRunViewable("Completed"), true);
  assert.equal(isRunViewable("PartiallyCompleted"), true);
  assert.equal(isRunPollable("Completed"), false);
  assert.equal(isRunPollable("PartiallyCompleted"), false);
});

test("Failed is terminal — not pollable, not viewable as a dashboard", () => {
  assert.equal(isRunPollable("Failed"), false);
  assert.equal(isRunViewable("Failed"), false);
});

test("only PartiallyCompleted is degraded — Completed is a full result", () => {
  assert.equal(isRunDegraded("PartiallyCompleted"), true);
  assert.equal(isRunDegraded("Completed"), false);
  assert.equal(isRunDegraded("Failed"), false);
});

test("describeRunState labels every status distinctly, tone reflects severity", () => {
  const requested = describeRunState("Requested", null);
  const running = describeRunState("Running", null);
  const completed = describeRunState("Completed", null);
  const partial = describeRunState("PartiallyCompleted", null);
  const failed = describeRunState("Failed", "social.niche_discovery.errors.provider_quota_exhausted");

  const labels = new Set([requested.label, running.label, completed.label, partial.label, failed.label]);
  assert.equal(labels.size, 5, "every status must render a distinct label");

  assert.equal(requested.tone, "neutral");
  assert.equal(running.tone, "neutral");
  assert.equal(completed.tone, "success");
  assert.equal(partial.tone, "warning");
  assert.equal(failed.tone, "error");
});

test("a Failed run's description carries its structured reason key, never invented text", () => {
  const failed = describeRunState("Failed", "social.niche_discovery.errors.provider_quota_exhausted");
  assert.equal(failed.failureReasonKey, "social.niche_discovery.errors.provider_quota_exhausted");

  const failedNoReason = describeRunState("Failed", null);
  assert.equal(failedNoReason.failureReasonKey, null);
});

test("PartiallyCompleted description explicitly flags degradation so the caller cannot present it as full", () => {
  const partial = describeRunState("PartiallyCompleted", null);
  assert.equal(partial.isDegraded, true);
  assert.equal(describeRunState("Completed", null).isDegraded, false);
});
