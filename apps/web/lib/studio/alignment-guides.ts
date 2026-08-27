/**
 * Pure, framework-free snap-candidate engine for the Content Studio drag/resize canvas (WU-8a,
 * design.md D-E). Given the dragged element's live rect, every sibling slot's rect, and the canvas
 * dimensions, computes a snapped position plus the guide lines to render — with no DOM access, no
 * React, and no knowledge of pointer events. WU-8b (a later batch) wires this into
 * `content-studio-panel.tsx`'s `onPointerMove` handler; this module only needs to be called with
 * canvas-coordinate rects (the caller already divides pointer delta by zoom scale, so the tolerance
 * below is zoom-invariant).
 *
 * Candidates evaluated per axis: the canvas's own centre, plus every sibling's three
 * start/centre/end positions on that axis (left/centreX/right for X, top/centreY/bottom for Y) —
 * six axes per sibling across both dimensions, per CLIF-CS-03. X and Y resolve independently: a
 * winner on one axis never blocks or is blocked by the other axis's candidates.
 */

export const SNAP_TOLERANCE_PX = 6;
export const SNAP_MAX_SIBLINGS = 24;

export interface Rect {
  left: number;
  top: number;
  width: number;
  height: number;
}

export interface GuideLine {
  axis: "x" | "y";
  position: number;
  from: number;
  to: number;
  source: "canvas" | "sibling";
}

export interface SnapResult {
  left: number;
  top: number;
  guides: readonly GuideLine[];
}

interface AxisCandidate {
  position: number;
  source: "canvas" | "sibling";
  /** Candidate's own edge kind (0 = left/top, 1 = centre, 2 = right/bottom) — used only for tie-break. */
  edgeOrder: 0 | 1 | 2;
}

interface AxisWinner {
  candidate: AxisCandidate;
  delta: number;
  draggedRefOrder: number;
}

const EDGE_START = 0 as const;
const EDGE_CENTER = 1 as const;
const EDGE_END = 2 as const;

/**
 * Computes the snapped `{left, top}` for `dragged` against the canvas centre and every sibling's
 * six edge/centre axes, within `tolerance` canvas px (default `SNAP_TOLERANCE_PX`). X and Y are
 * resolved independently. When neither axis has a candidate within tolerance, the returned position
 * is unchanged and `guides` is empty — snapping is test-distinguishable from free movement
 * (CLIF-CS-04).
 */
export function computeSnap(input: {
  dragged: Rect;
  siblings: readonly Rect[];
  canvas: { width: number; height: number };
  tolerance?: number;
}): SnapResult {
  const { dragged, siblings, canvas } = input;
  const tolerance = input.tolerance ?? SNAP_TOLERANCE_PX;

  const xCandidates = buildAxisCandidates(
    canvas.width / 2,
    siblings.map((s) => ({ start: s.left, center: s.left + s.width / 2, end: s.left + s.width })),
  );
  const yCandidates = buildAxisCandidates(
    canvas.height / 2,
    siblings.map((s) => ({ start: s.top, center: s.top + s.height / 2, end: s.top + s.height })),
  );

  const draggedXRefs = [dragged.left, dragged.left + dragged.width / 2, dragged.left + dragged.width];
  const draggedYRefs = [dragged.top, dragged.top + dragged.height / 2, dragged.top + dragged.height];

  const xWinner = resolveAxis(xCandidates, draggedXRefs, tolerance);
  const yWinner = resolveAxis(yCandidates, draggedYRefs, tolerance);

  const guides: GuideLine[] = [];
  let left = dragged.left;
  let top = dragged.top;

  if (xWinner) {
    left = dragged.left + xWinner.delta;
    guides.push({
      axis: "x",
      position: xWinner.candidate.position,
      from: 0,
      to: canvas.height,
      source: xWinner.candidate.source,
    });
  }

  if (yWinner) {
    top = dragged.top + yWinner.delta;
    guides.push({
      axis: "y",
      position: yWinner.candidate.position,
      from: 0,
      to: canvas.width,
      source: yWinner.candidate.source,
    });
  }

  return { left, top, guides };
}

/** Maps a `GuideLine` (canvas coordinates) to an absolutely-positioned 1px overlay div style. */
export function guideToOverlayStyle(
  g: GuideLine,
  scale: number,
): { top: number; left: number; width: number; height: number } {
  if (g.axis === "x") {
    return {
      left: g.position * scale,
      top: g.from * scale,
      width: 1,
      height: (g.to - g.from) * scale,
    };
  }

  return {
    left: g.from * scale,
    top: g.position * scale,
    width: (g.to - g.from) * scale,
    height: 1,
  };
}

function buildAxisCandidates(
  canvasCenter: number,
  siblingPositions: readonly { start: number; center: number; end: number }[],
): AxisCandidate[] {
  const candidates: AxisCandidate[] = [{ position: canvasCenter, source: "canvas", edgeOrder: EDGE_CENTER }];

  for (const s of siblingPositions) {
    candidates.push(
      { position: s.start, source: "sibling", edgeOrder: EDGE_START },
      { position: s.center, source: "sibling", edgeOrder: EDGE_CENTER },
      { position: s.end, source: "sibling", edgeOrder: EDGE_END },
    );
  }

  return candidates;
}

/**
 * Finds the winning candidate (if any) within `tolerance` of any of the dragged element's three
 * reference positions on this axis. Deterministic tie-break (design.md D-E): smallest `|delta|`
 * first; then canvas before sibling; then smaller candidate position; then candidate edge order
 * (start < centre < end); then the dragged reference's own edge order, as a final stabilizer.
 */
function resolveAxis(
  candidates: readonly AxisCandidate[],
  draggedRefs: readonly number[],
  tolerance: number,
): AxisWinner | null {
  let best: AxisWinner | null = null;

  for (const candidate of candidates) {
    for (let draggedRefOrder = 0; draggedRefOrder < draggedRefs.length; draggedRefOrder++) {
      const delta = candidate.position - draggedRefs[draggedRefOrder];
      if (Math.abs(delta) > tolerance) continue;

      const contender: AxisWinner = { candidate, delta, draggedRefOrder };
      if (!best || isBetterCandidate(contender, best)) best = contender;
    }
  }

  return best;
}

function isBetterCandidate(a: AxisWinner, b: AxisWinner): boolean {
  const distA = Math.abs(a.delta);
  const distB = Math.abs(b.delta);
  if (distA !== distB) return distA < distB;

  if (a.candidate.source !== b.candidate.source) return a.candidate.source === "canvas";

  if (a.candidate.position !== b.candidate.position) return a.candidate.position < b.candidate.position;

  if (a.candidate.edgeOrder !== b.candidate.edgeOrder) return a.candidate.edgeOrder < b.candidate.edgeOrder;

  return a.draggedRefOrder < b.draggedRefOrder;
}
