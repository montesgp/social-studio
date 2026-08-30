import assert from "node:assert/strict";
import { test } from "node:test";
import {
  buildEnqueueCandidates,
  compositionReducer,
  getFormatCount,
  initialCompositionState,
  isCandidateComposed,
  validateComposition
} from "./workflow-composition";

test("set-count adds a format entry for a candidate", () => {
  const state = compositionReducer(initialCompositionState, {
    type: "set-count",
    candidateId: "c1",
    formatKey: "reel-script",
    count: 2
  });

  assert.equal(getFormatCount(state, "c1", "reel-script"), 2);
  assert.equal(isCandidateComposed(state, "c1"), true);
});

test("set-count with count 0 removes the format and leaves candidate uncomposed", () => {
  let state = compositionReducer(initialCompositionState, {
    type: "set-count",
    candidateId: "c1",
    formatKey: "reel-script",
    count: 2
  });
  state = compositionReducer(state, { type: "set-count", candidateId: "c1", formatKey: "reel-script", count: 0 });

  assert.equal(getFormatCount(state, "c1", "reel-script"), 0);
  assert.equal(isCandidateComposed(state, "c1"), false);
  assert.deepEqual(state, {});
});

test("different candidates keep independent compositions", () => {
  let state = compositionReducer(initialCompositionState, {
    type: "set-count",
    candidateId: "c1",
    formatKey: "reel-script",
    count: 1
  });
  state = compositionReducer(state, { type: "set-count", candidateId: "c2", formatKey: "caption", count: 3 });

  assert.equal(getFormatCount(state, "c1", "reel-script"), 1);
  assert.equal(getFormatCount(state, "c1", "caption"), 0);
  assert.equal(getFormatCount(state, "c2", "caption"), 3);
});

test("clear-candidate removes only that candidate's composition", () => {
  let state = compositionReducer(initialCompositionState, {
    type: "set-count",
    candidateId: "c1",
    formatKey: "reel-script",
    count: 1
  });
  state = compositionReducer(state, { type: "set-count", candidateId: "c2", formatKey: "caption", count: 1 });
  state = compositionReducer(state, { type: "clear-candidate", candidateId: "c1" });

  assert.equal(isCandidateComposed(state, "c1"), false);
  assert.equal(isCandidateComposed(state, "c2"), true);
});

test("reset clears every candidate", () => {
  let state = compositionReducer(initialCompositionState, {
    type: "set-count",
    candidateId: "c1",
    formatKey: "reel-script",
    count: 1
  });
  state = compositionReducer(state, { type: "reset" });

  assert.deepEqual(state, {});
});

test("validateComposition: two selected candidates each with formats -> valid, previews independent per spec", () => {
  let state = compositionReducer(initialCompositionState, {
    type: "set-count",
    candidateId: "c1",
    formatKey: "reel-script",
    count: 1
  });
  state = compositionReducer(state, { type: "set-count", candidateId: "c2", formatKey: "caption", count: 1 });

  const result = validateComposition(state, ["c1", "c2"]);
  assert.equal(result.valid, true);
  assert.deepEqual(result.emptyCandidateIds, []);

  const candidates = buildEnqueueCandidates(state, ["c1", "c2"]);
  assert.equal(candidates.length, 2);
  assert.deepEqual(
    candidates.find((c) => c.candidateId === "c1")?.composition,
    [{ formatKey: "reel-script", count: 1 }]
  );
  assert.deepEqual(
    candidates.find((c) => c.candidateId === "c2")?.composition,
    [{ formatKey: "caption", count: 1 }]
  );
});

test("validateComposition: a selected candidate with zero formats blocks enqueue", () => {
  const state = compositionReducer(initialCompositionState, {
    type: "set-count",
    candidateId: "c1",
    formatKey: "reel-script",
    count: 1
  });

  const result = validateComposition(state, ["c1", "c2"]);
  assert.equal(result.valid, false);
  assert.deepEqual(result.emptyCandidateIds, ["c2"]);
});

test("buildEnqueueCandidates excludes uncomposed candidates from the payload", () => {
  const state = compositionReducer(initialCompositionState, {
    type: "set-count",
    candidateId: "c1",
    formatKey: "reel-script",
    count: 1
  });

  const candidates = buildEnqueueCandidates(state, ["c1", "c2"]);
  assert.equal(candidates.length, 1);
  assert.equal(candidates[0]?.candidateId, "c1");
});
