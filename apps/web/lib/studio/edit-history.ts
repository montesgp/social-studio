/**
 * Pure undo/redo policy for the content-studio design canvas. Kept free of DOM/React so it can be
 * unit-tested with the project's `tsx --test` runner (no jsdom in this repo); the imperative canvas
 * wiring in `content-studio-panel.tsx` calls into this with real markup strings captured via
 * `DesignCanvasHandle.getState()`.
 *
 * All policy lives here: the `{past, present, future}` stack reducer, the per-slide registry, the
 * typing-coalescing boundary, and the keyboard-shortcut predicate. The panel component only wires
 * these together — see the glue-site audit in the design doc for what "wiring only" means.
 */

/** Above a fluent typist's inter-keystroke gap (~120-250ms), below a deliberate pause — one typing burst = one entry. */
export const TYPING_COALESCE_WINDOW_MS = 800;

/** Per-slide stack cap. FIFO eviction of the oldest `past` entry once exceeded — see R9. */
export const MAX_HISTORY_DEPTH = 50;

export type EditKind = "typing" | "structural";

/** What the DOM glue reports about a capture, before it becomes a `Snapshot`. */
export interface EditSignal {
  kind: EditKind;
  slot: string | null;
}

export interface Snapshot {
  markup: string;
  kind: EditKind;
  slot: string | null;
  at: number;
}

export interface HistoryState {
  past: Snapshot[];
  present: Snapshot;
  future: Snapshot[];
}

export type HistoryRegistry = ReadonlyMap<number, HistoryState>;

/** Seeds a fresh per-slide stack. `baseline` is the pre-edit markup (DD4) — no history yet. */
export function createHistory(baseline: Snapshot): HistoryState {
  return { past: [], present: baseline, future: [] };
}

/**
 * Pushes or replaces `state.present` with `next`, per the coalescing policy (R2), and always clears
 * `future` (a new commit invalidates any pending redo, R3.S3). The FIFO depth cap (R9) is enforced on
 * `past` only — coalescing replaces `present` in place and never grows `past`.
 */
export function commit(state: HistoryState, next: Snapshot): HistoryState {
  if (shouldCoalesce(state.present, next)) {
    return { past: state.past, present: next, future: [] };
  }

  const past = [...state.past, state.present];
  if (past.length > MAX_HISTORY_DEPTH) past.shift();
  return { past, present: next, future: [] };
}

/** Steps one entry back. No-op (returns the same state, `restored: null`) when `past` is empty. */
export function undo(state: HistoryState): { state: HistoryState; restored: Snapshot | null } {
  if (state.past.length === 0) return { state, restored: null };

  const past = state.past.slice(0, -1);
  const restored = state.past[state.past.length - 1];
  return { state: { past, present: restored, future: [state.present, ...state.future] }, restored };
}

/** Steps one entry forward. No-op (returns the same state, `restored: null`) when `future` is empty. */
export function redo(state: HistoryState): { state: HistoryState; restored: Snapshot | null } {
  if (state.future.length === 0) return { state, restored: null };

  const [restored, ...future] = state.future;
  return { state: { past: [...state.past, state.present], present: restored, future }, restored };
}

export function canUndo(state: HistoryState | undefined): boolean {
  return (state?.past.length ?? 0) > 0;
}

export function canRedo(state: HistoryState | undefined): boolean {
  return (state?.future.length ?? 0) > 0;
}

/**
 * Decides push vs. replace for a new capture against the current `present` (R2). Replace only when
 * both captures are typing captures on the same non-null slot, within the coalescing window. Any
 * other combination pushes, including a non-typing capture that closes an open typing entry.
 */
export function shouldCoalesce(prev: Snapshot, next: Snapshot): boolean {
  if (prev.kind !== "typing" || next.kind !== "typing") return false;
  if (prev.slot === null || next.slot === null) return false;
  if (prev.slot !== next.slot) return false;
  return next.at - prev.at < TYPING_COALESCE_WINDOW_MS;
}

/** Returns a new registry with `idx`'s stack set to `s`. Never mutates `reg` (no aliasing across slides). */
export function withStack(reg: HistoryRegistry, idx: number, s: HistoryState): HistoryRegistry {
  const next = new Map(reg);
  next.set(idx, s);
  return next;
}

/** Slide count changes misalign index-keyed stacks (R4) — the registry must be discarded wholesale. */
export function shouldClearRegistry(prevSlideCount: number, nextSlideCount: number): boolean {
  return prevSlideCount !== nextSlideCount;
}

export type ShortcutAction = "undo" | "redo" | "delete" | "none";

/**
 * Decides whether a keydown drives the custom stack (R8). While a slot is in text-editing mode
 * (`contenteditable` active), native undo/redo must win — this returns `"none"` unconditionally so
 * the caller falls through instead of intercepting.
 *
 * Delete/Backspace remove the selected element. They carry NO modifier, so they are resolved before
 * the `ctrlOrMeta` gate below — that gate used to reject every unmodified key, which is why the only
 * way to delete a component was the toolbar's trash button. While text is being edited these keys
 * must keep deleting characters, which the `isTextEditing` guard above already ensures.
 */
export function resolveShortcut(i: {
  key: string;
  ctrlOrMeta: boolean;
  shift: boolean;
  isTextEditing: boolean;
}): ShortcutAction {
  if (i.isTextEditing) return "none";
  if (!i.ctrlOrMeta && (i.key === "Delete" || i.key === "Backspace")) return "delete";
  if (!i.ctrlOrMeta) return "none";
  if (i.key.toLowerCase() !== "z") return "none";
  return i.shift ? "redo" : "undo";
}
