import { test } from "node:test";
import assert from "node:assert/strict";

import {
  CONTENT_FORMAT_OPTIONS,
  DEPTH_OPTIONS,
  DEFAULT_WINDOW_DAYS,
  MAX_WINDOW_DAYS,
  MIN_WINDOW_DAYS,
  buildStartRunRequest,
  createDefaultStartRunFormState,
  validateStartRunForm,
  type StartRunFormState,
} from "./start-run-form";

// WU-14 — the analysis window and depth are operator-chosen, explicit owner decisions (not
// defaults of convenience), with a 90-day window default (Incoders.Hive.Domain DiscoveryWindow:
// MinDays=7, MaxDays=365, DefaultDays=90). This module is the only place that encodes those bounds
// for the frontend.

function validState(): StartRunFormState {
  return {
    sourceMarketCode: "mkt-one",
    targetMarketCode: "mkt-two",
    networkKey: "youtube",
    contentFormat: "video",
    windowDays: 90,
    depth: "Standard",
  };
}

test("createDefaultStartRunFormState defaults the window to 90 days and depth to Standard", () => {
  const state = createDefaultStartRunFormState();
  assert.equal(state.windowDays, DEFAULT_WINDOW_DAYS);
  assert.equal(state.windowDays, 90);
  assert.equal(state.depth, "Standard");
});

test("depth and content format options are exactly the operator-selectable values", () => {
  assert.deepEqual(DEPTH_OPTIONS, ["Quick", "Standard", "Deep"]);
  assert.deepEqual(CONTENT_FORMAT_OPTIONS, ["video", "short"]);
});

test("window day bounds match the backend DiscoveryWindow value object (7..365)", () => {
  assert.equal(MIN_WINDOW_DAYS, 7);
  assert.equal(MAX_WINDOW_DAYS, 365);
});

test("a fully filled, in-range form is valid", () => {
  const result = validateStartRunForm(validState());
  assert.equal(result.valid, true);
  assert.deepEqual(result.errors, []);
});

test("missing source or target market is rejected", () => {
  const missingSource = validateStartRunForm({ ...validState(), sourceMarketCode: "" });
  assert.equal(missingSource.valid, false);
  assert.ok(missingSource.errors.includes("sourceMarketCode"));

  const missingTarget = validateStartRunForm({ ...validState(), targetMarketCode: "" });
  assert.equal(missingTarget.valid, false);
  assert.ok(missingTarget.errors.includes("targetMarketCode"));
});

test("source and target market must differ", () => {
  const result = validateStartRunForm({ ...validState(), targetMarketCode: "mkt-one" });
  assert.equal(result.valid, false);
  assert.ok(result.errors.includes("marketsMustDiffer"));
});

test("window days outside 7..365 is rejected", () => {
  const tooLow = validateStartRunForm({ ...validState(), windowDays: 6 });
  assert.equal(tooLow.valid, false);
  assert.ok(tooLow.errors.includes("windowDays"));

  const tooHigh = validateStartRunForm({ ...validState(), windowDays: 366 });
  assert.equal(tooHigh.valid, false);
  assert.ok(tooHigh.errors.includes("windowDays"));

  const boundaryLow = validateStartRunForm({ ...validState(), windowDays: 7 });
  assert.equal(boundaryLow.valid, true);
  const boundaryHigh = validateStartRunForm({ ...validState(), windowDays: 365 });
  assert.equal(boundaryHigh.valid, true);
});

test("buildStartRunRequest maps a valid form 1:1 onto the API request shape", () => {
  const request = buildStartRunRequest(validState());
  assert.deepEqual(request, {
    sourceMarketCode: "mkt-one",
    targetMarketCode: "mkt-two",
    networkKey: "youtube",
    contentFormat: "video",
    windowDays: 90,
    depth: "Standard",
  });
});
