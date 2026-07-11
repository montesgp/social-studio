import assert from "node:assert/strict";
import test from "node:test";

import {
  generationHistoryLabels,
  shouldAutoSwitchToPackage,
  shouldRefreshTerminalHistory,
  packageProgressState,
  PACKAGE_STEP_COUNT,
} from "./single-analyzer-ui-state.ts";

test("preserves Generated and describes the current regeneration attempt", () => {
  assert.deepEqual(generationHistoryLabels(true, "Pending"), ["Generated", "Regeneration queued"]);
  assert.deepEqual(generationHistoryLabels(true, "Running"), ["Generated", "Regenerating…"]);
  assert.deepEqual(generationHistoryLabels(true, "Partial"), ["Generated", "Regeneration partial"]);
  assert.deepEqual(generationHistoryLabels(true, "Failed"), ["Generated", "Regeneration failed"]);
  assert.deepEqual(generationHistoryLabels(true, "Completed"), ["Generated"]);
});

test("does not consume a new job refresh key with the previous job terminal state", () => {
  const activeJobId = "new-job";

  assert.equal(shouldRefreshTerminalHistory({
    activeJobId,
    observedJobId: "old-job",
    status: "Completed",
    lastRefreshKey: "old-job:Completed",
  }), false);

  assert.equal(shouldRefreshTerminalHistory({
    activeJobId,
    observedJobId: activeJobId,
    status: "Completed",
    lastRefreshKey: "old-job:Completed",
  }), true);

  assert.equal(shouldRefreshTerminalHistory({
    activeJobId,
    observedJobId: activeJobId,
    status: "Completed",
    lastRefreshKey: "new-job:Completed",
  }), false);
});

test("keeps generation wording when no previous package exists", () => {
  assert.deepEqual(generationHistoryLabels(false, "Failed"), ["Generation failed"]);
  assert.deepEqual(generationHistoryLabels(false, "Running"), ["Generating…"]);
});

test("auto-switches only after the current user-started job reports its own terminal state", () => {
  const base = {
    activeJobId: "new-job",
    userStartedJobId: "new-job",
    alreadySwitchedJobId: null,
  };

  assert.equal(shouldAutoSwitchToPackage({ ...base, observedJobId: "old-job", status: "Completed" }), false);
  assert.equal(shouldAutoSwitchToPackage({ ...base, observedJobId: "new-job", status: "Running" }), false);
  assert.equal(shouldAutoSwitchToPackage({ ...base, observedJobId: "new-job", status: "Completed" }), true);
  assert.equal(shouldAutoSwitchToPackage({ ...base, observedJobId: "new-job", status: "Partial" }), true);
  assert.equal(shouldAutoSwitchToPackage({ ...base, observedJobId: "new-job", status: "Completed", alreadySwitchedJobId: "new-job" }), false);
  assert.equal(shouldAutoSwitchToPackage({ ...base, observedJobId: "new-job", status: "Completed", userStartedJobId: null }), false);
});

// issue #67 (BAPA-STATUS-03): overlay shows a distinct "Analyzing profile" phase while status is
// AnalyzingProfile, with later steps pending, and transitions out once status changes.
test("packageProgressState shows the Analyzing profile phase with later steps pending", () => {
  const state = packageProgressState("AnalyzingProfile", {}, 3);

  assert.equal(state.label, "Analyzing profile aesthetic");
  assert.equal(state.completedFormats, 0);
  // currentStep === 1 means every step from index 1 onward (ai/render/zip) is still
  // pending/blocked in the overlay's step renderer (index < currentStep = done).
  assert.equal(state.currentStep, 1);
  assert.ok(state.currentStep < PACKAGE_STEP_COUNT);
});

test("packageProgressState transitions out of Analyzing profile once status changes", () => {
  const runningState = packageProgressState("Running", {}, 3);
  const completedState = packageProgressState("Completed", {}, 3);
  const partialState = packageProgressState("Partial", {}, 3);
  const failedState = packageProgressState("Failed", {}, 3);

  for (const state of [runningState, completedState, partialState, failedState]) {
    assert.notEqual(state.label, "Analyzing profile aesthetic");
  }
  assert.equal(completedState.currentStep, PACKAGE_STEP_COUNT);
  assert.equal(partialState.currentStep, PACKAGE_STEP_COUNT);
});

test("packageProgressState Queued/Running/terminal states are unaffected by the AnalyzingProfile branch", () => {
  assert.equal(packageProgressState("Queued", {}, 3).label, "Queued");
  assert.equal(packageProgressState(null, {}, 3).label, "Initializing generation request");
  assert.equal(packageProgressState("Running", {}, 3).label, "Preparing analysis context");
});
