import assert from "node:assert/strict";
import test from "node:test";

import {
  activeStageIndex,
  activeStageLabel,
  resolveStageStatuses,
  type OverlayStage,
} from "./stage-overlay-state.ts";

const STAGES: OverlayStage[] = [
  { key: "queued", label: "Queued" },
  { key: "fetch", label: "Fetching content" },
  { key: "ready", label: "Content ready" },
  { key: "ai", label: "AI analysis" },
];

test("activeStageIndex resolves the matching stage's index", () => {
  assert.equal(activeStageIndex(STAGES, "fetch"), 1);
  assert.equal(activeStageIndex(STAGES, "ai"), 3);
});

test("activeStageIndex falls back to the first stage for a missing or unknown key", () => {
  assert.equal(activeStageIndex(STAGES, null), 0);
  assert.equal(activeStageIndex(STAGES, undefined), 0);
  assert.equal(activeStageIndex(STAGES, "not-a-real-stage"), 0);
});

test("resolveStageStatuses marks earlier stages done, the active one active, later ones pending", () => {
  assert.deepEqual(resolveStageStatuses(STAGES, 2, "running"), [
    "done",
    "done",
    "active",
    "pending",
  ]);
});

test("resolveStageStatuses treats the first stage as active when it is the active index", () => {
  assert.deepEqual(resolveStageStatuses(STAGES, 0, "running"), [
    "active",
    "pending",
    "pending",
    "pending",
  ]);
});

test("resolveStageStatuses marks every stage done on a terminal outcome, success or failure", () => {
  assert.deepEqual(resolveStageStatuses(STAGES, STAGES.length, "done"), [
    "done",
    "done",
    "done",
    "done",
  ]);
  assert.deepEqual(resolveStageStatuses(STAGES, STAGES.length, "failed"), [
    "done",
    "done",
    "done",
    "done",
  ]);
});

test("activeStageLabel returns the active stage's label", () => {
  assert.equal(activeStageLabel(STAGES, 1), "Fetching content");
});

test("activeStageLabel clamps out-of-range indices to the list bounds", () => {
  assert.equal(activeStageLabel(STAGES, -1), "Queued");
  assert.equal(activeStageLabel(STAGES, 99), "AI analysis");
});

test("activeStageLabel returns an empty string for an empty stage list", () => {
  assert.equal(activeStageLabel([], 0), "");
});
