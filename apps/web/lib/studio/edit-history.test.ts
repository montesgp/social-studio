import { test } from "node:test";
import assert from "node:assert/strict";
import {
  commit,
  createHistory,
  undo,
  redo,
  MAX_HISTORY_DEPTH,
  shouldCoalesce,
  withStack,
  shouldClearRegistry,
  resolveShortcut,
} from "./edit-history.ts";
import type { HistoryRegistry, HistoryState, Snapshot } from "./edit-history.ts";

/**
 * Node-runner coverage for the pure edit-history module (undo/redo for the content-studio design
 * canvas). No jsdom/browser is available in this repo's test runner — every scenario here exercises
 * the reducer, registry, and predicates directly with plain objects, matching the project's existing
 * `lib/studio/*.test.ts` convention.
 */

function snap(over: Partial<Snapshot> = {}): Snapshot {
  return { markup: "<div>a</div>", kind: "structural", slot: null, at: 0, ...over };
}

test("commit pushes a distinct entry for two different style mutations", () => {
  const h0 = createHistory(snap({ markup: "base" }));
  const h1 = commit(h0, snap({ markup: "bold-applied", at: 100 }));
  const h2 = commit(h1, snap({ markup: "color-applied", at: 200 }));

  assert.equal(h2.past.length, 2);
  assert.deepEqual(
    h2.past.map((s) => s.markup),
    ["base", "bold-applied"],
  );
  assert.equal(h2.present.markup, "color-applied");
});

test("commit after an undo clears future (R1.S1, R3.S3)", () => {
  const h0 = createHistory(snap({ markup: "base" }));
  const h1 = commit(h0, snap({ markup: "edit-1", at: 100 }));
  const { state: h2 } = undo(h1);
  assert.equal(h2.future.length, 1);

  const h3 = commit(h2, snap({ markup: "edit-2", at: 300 }));
  assert.equal(h3.future.length, 0);
  assert.equal(h3.present.markup, "edit-2");
});

test("undo/redo round-trip", () => {
  const h0 = createHistory(snap({ markup: "base" }));
  const h1 = commit(h0, snap({ markup: "edit-1", at: 100 }));

  const { state: h2, restored: undoRestored } = undo(h1);
  assert.equal(undoRestored?.markup, "base");
  assert.equal(h2.present.markup, "base");

  const { state: h3, restored: redoRestored } = redo(h2);
  assert.equal(redoRestored?.markup, "edit-1");
  assert.equal(h3.present.markup, "edit-1");
});

test("undo on empty past is a no-op returning restored: null", () => {
  const h0 = createHistory(snap({ markup: "base" }));
  const { state, restored } = undo(h0);
  assert.equal(restored, null);
  assert.deepEqual(state, h0);
});

test("redo on empty future is a no-op returning restored: null", () => {
  const h0 = createHistory(snap({ markup: "base" }));
  const h1 = commit(h0, snap({ markup: "edit-1", at: 100 }));
  const { state, restored } = redo(h1);
  assert.equal(restored, null);
  assert.deepEqual(state, h1);
});

test("commit at the depth cap keeps past.length at MAX_HISTORY_DEPTH and drops the oldest entry (FIFO)", () => {
  let h = createHistory(snap({ markup: "seed-0" }));
  for (let i = 1; i <= MAX_HISTORY_DEPTH; i += 1) {
    h = commit(h, snap({ markup: `edit-${i}`, at: i, kind: "structural" }));
  }
  assert.equal(h.past.length, MAX_HISTORY_DEPTH);
  assert.equal(h.past[0]?.markup, "seed-0");

  const overflowed = commit(h, snap({ markup: "edit-overflow", at: MAX_HISTORY_DEPTH + 1, kind: "structural" }));
  assert.equal(overflowed.past.length, MAX_HISTORY_DEPTH);
  assert.equal(overflowed.past[0]?.markup, "edit-1");
  assert.equal(overflowed.past[overflowed.past.length - 1]?.markup, `edit-${MAX_HISTORY_DEPTH}`);
});

test("undo past the cap terminates cleanly via the empty-past no-op, never referencing a dropped entry", () => {
  let h = createHistory(snap({ markup: "seed-0" }));
  for (let i = 1; i <= MAX_HISTORY_DEPTH + 5; i += 1) {
    h = commit(h, snap({ markup: `edit-${i}`, at: i, kind: "structural" }));
  }
  for (let i = 0; i < MAX_HISTORY_DEPTH + 10; i += 1) {
    const result = undo(h);
    h = result.state;
  }
  assert.equal(h.past.length, 0);
  assert.equal(canUndoOf(h), false);
});

function canUndoOf(state: HistoryState): boolean {
  return state.past.length > 0;
}

// --- shouldCoalesce (R2) ---

test("shouldCoalesce: same slot within the window replaces (returns true)", () => {
  const prev = snap({ kind: "typing", slot: "caption-1", at: 0 });
  const next = snap({ kind: "typing", slot: "caption-1", at: 200 });
  assert.equal(shouldCoalesce(prev, next), true);
});

test("shouldCoalesce: same slot past the window pushes (returns false)", () => {
  const prev = snap({ kind: "typing", slot: "caption-1", at: 0 });
  const next = snap({ kind: "typing", slot: "caption-1", at: 900 });
  assert.equal(shouldCoalesce(prev, next), false);
});

test("shouldCoalesce: slot A -> slot B within the window pushes — slot mismatch overrides timing", () => {
  const prev = snap({ kind: "typing", slot: "caption-1", at: 0 });
  const next = snap({ kind: "typing", slot: "caption-2", at: 100 });
  assert.equal(shouldCoalesce(prev, next), false);
});

test("shouldCoalesce: a typing capture followed by a structural capture never coalesces", () => {
  const prev = snap({ kind: "typing", slot: "caption-1", at: 0 });
  const next = snap({ kind: "structural", slot: null, at: 100 });
  assert.equal(shouldCoalesce(prev, next), false);
});

// --- per-slide registry (R4) ---

test("withStack: slide A's stack is untouched after slide B is edited via a separate withStack call", () => {
  const reg0: HistoryRegistry = new Map();
  const stackA = createHistory(snap({ markup: "A-base" }));
  const reg1 = withStack(reg0, 0, stackA);

  const stackB = createHistory(snap({ markup: "B-base" }));
  const reg2 = withStack(reg1, 1, stackB);

  assert.deepEqual(reg2.get(0), stackA);
  assert.deepEqual(reg2.get(1), stackB);
});

test("withStack: an A -> B -> A round trip yields a stack for A identical to before the round trip", () => {
  const reg0: HistoryRegistry = new Map();
  const stackA = commit(createHistory(snap({ markup: "A-base" })), snap({ markup: "A-edit", at: 10 }));
  const reg1 = withStack(reg0, 0, stackA);
  const reg2 = withStack(reg1, 1, createHistory(snap({ markup: "B-base" })));
  const reg3 = withStack(reg2, 1, commit(reg2.get(1)!, snap({ markup: "B-edit", at: 20 })));

  const stackAAfterRoundTrip = reg3.get(0);
  assert.deepEqual(stackAAfterRoundTrip, stackA);
});

test("shouldClearRegistry: true when slide counts differ", () => {
  assert.equal(shouldClearRegistry(3, 4), true);
  assert.equal(shouldClearRegistry(3, 2), true);
});

test("shouldClearRegistry: false when slide counts are equal", () => {
  assert.equal(shouldClearRegistry(3, 3), false);
});

// --- resolveShortcut (R8) ---

test("resolveShortcut: Ctrl/Cmd+Z outside text-editing resolves to undo", () => {
  assert.equal(resolveShortcut({ isTextEditing: false, key: "z", ctrlOrMeta: true, shift: false }), "undo");
});

test("resolveShortcut: Ctrl/Cmd+Shift+Z outside text-editing resolves to redo", () => {
  assert.equal(resolveShortcut({ isTextEditing: false, key: "z", ctrlOrMeta: true, shift: true }), "redo");
});

test("resolveShortcut: isTextEditing true resolves to none regardless of modifiers", () => {
  assert.equal(resolveShortcut({ isTextEditing: true, key: "z", ctrlOrMeta: true, shift: false }), "none");
  assert.equal(resolveShortcut({ isTextEditing: true, key: "z", ctrlOrMeta: true, shift: true }), "none");
});

test("resolveShortcut: ctrlOrMeta false resolves to none regardless of isTextEditing/shift", () => {
  assert.equal(resolveShortcut({ isTextEditing: false, key: "z", ctrlOrMeta: false, shift: false }), "none");
  assert.equal(resolveShortcut({ isTextEditing: true, key: "z", ctrlOrMeta: false, shift: true }), "none");
});

test("resolveShortcut: plain z with no modifier resolves to none", () => {
  assert.equal(resolveShortcut({ isTextEditing: false, key: "z", ctrlOrMeta: false, shift: false }), "none");
});

test("resolveShortcut: a non-z key resolves to none even with modifiers", () => {
  assert.equal(resolveShortcut({ isTextEditing: false, key: "a", ctrlOrMeta: true, shift: false }), "none");
});
