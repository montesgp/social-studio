// ── Shared stage-overlay logic ───────────────────────────────────────────────
// Pure, render-free rules for the single full-screen "stage overlay" used across the app
// (initial tool loading, analysis/discovery polling, package generation, profile analysis...).
// Extracted so the done/active/pending mapping is unit-testable without mounting anything
// (see stage-overlay-state.test.ts), following the convention in single-analyzer-ui-state.ts.
//
// Deliberately has NO concept of a numeric completion percentage: an overlay only ever knows
// which real stage it is currently in, never a fabricated "% done".

export interface OverlayStage {
  key: string;
  label: string;
}

export type StageStatus = "done" | "active" | "pending";

export type OverlayOutcome = "running" | "done" | "failed";

/**
 * Resolves the 0-based index of the active stage from its key.
 * Falls back to the first stage when the key is missing/unknown so the overlay
 * never renders with no active stage highlighted.
 */
export function activeStageIndex(stages: readonly OverlayStage[], activeKey: string | null | undefined): number {
  if (!activeKey) return 0;
  const idx = stages.findIndex((stage) => stage.key === activeKey);
  return idx === -1 ? 0 : idx;
}

/**
 * Per-stage visual status for the ordered list.
 *
 * On a terminal outcome (done/failed) every stage renders as "done" — this mirrors the previous
 * AnalysisProgressCard behavior (callers always passed currentStep === stages.length on any
 * terminal state), so the header icon/title alone carries the success/failure signal instead of
 * marking one particular stage as failed.
 */
export function resolveStageStatuses(
  stages: readonly OverlayStage[],
  activeIndex: number,
  outcome: OverlayOutcome
): StageStatus[] {
  if (outcome !== "running") {
    return stages.map(() => "done");
  }

  return stages.map((_, i) => {
    if (i < activeIndex) return "done";
    if (i === activeIndex) return "active";
    return "pending";
  });
}

/** Label of the currently active stage, clamped to the list bounds. */
export function activeStageLabel(stages: readonly OverlayStage[], activeIndex: number): string {
  if (stages.length === 0) return "";
  const clamped = Math.min(Math.max(activeIndex, 0), stages.length - 1);
  return stages[clamped]?.label ?? "";
}
