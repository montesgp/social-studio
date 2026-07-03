import assert from "node:assert/strict";
import test from "node:test";

import {
  generationHistoryLabels,
  shouldAutoSwitchToPackage,
  shouldRefreshTerminalHistory,
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
