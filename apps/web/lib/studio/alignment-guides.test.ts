import { test } from "node:test";
import assert from "node:assert/strict";
import {
  computeSnap,
  guideToOverlayStyle,
  offsetBoxesToRects,
  SNAP_MAX_SIBLINGS,
  SNAP_TOLERANCE_PX,
} from "./alignment-guides.ts";
import type { OffsetBox, Rect } from "./alignment-guides.ts";

/**
 * Unit coverage for the pure snap-candidate engine (WU-8a, design.md D-E). WU-8b wires
 * `computeSnap`/`guideToOverlayStyle` into `content-studio-panel.tsx`'s pointer handlers and is
 * owner-verified manually — this repo has no component-render harness (see design.md's Testing
 * Strategy). These cases are the full acceptance surface for CLIF-CS-02 through CLIF-CS-05.
 */

const CANVAS = { width: 1000, height: 800 };

function rect(left: number, top: number, width: number, height: number): Rect {
  return { left, top, width, height };
}

// ── CLIF-CS-02: canvas-centre snap ─────────────────────────────────────────────

test("computeSnap snaps to canvas centre-x when within tolerance and renders a vertical guide (CLIF-CS-02-a)", () => {
  // dragged centreX = 447 + 50 = 497; canvas centreX = 500; delta = 3 (within 6px tolerance)
  const dragged = rect(447, 0, 100, 10);
  const result = computeSnap({ dragged, siblings: [], canvas: CANVAS });

  assert.equal(result.left, 450); // centreX becomes exactly 500
  assert.equal(result.top, dragged.top); // y untouched — nothing near canvas centre-y
  assert.equal(result.guides.length, 1);
  assert.deepEqual(result.guides[0], { axis: "x", position: 500, from: 0, to: CANVAS.height, source: "canvas" });
});

test("computeSnap snaps to canvas centre-y when within tolerance and renders a horizontal guide (CLIF-CS-02-b)", () => {
  // dragged centreY = 347 + 50 = 397; canvas centreY = 400; delta = 3 (within tolerance)
  const dragged = rect(0, 347, 10, 100);
  const result = computeSnap({ dragged, siblings: [], canvas: CANVAS });

  assert.equal(result.top, 350); // centreY becomes exactly 400
  assert.equal(result.left, dragged.left); // x untouched
  assert.equal(result.guides.length, 1);
  assert.deepEqual(result.guides[0], { axis: "y", position: 400, from: 0, to: CANVAS.width, source: "canvas" });
});

// ── CLIF-CS-03: per-sibling six-axis snap ───────────────────────────────────────

test("computeSnap snaps the dragged element's left edge to a sibling's left edge (CLIF-CS-03-a)", () => {
  const sibling = rect(200, 500, 120, 60);
  // dragged.left = 204, 4px from sibling.left = 200 (within tolerance); y kept far from any candidate
  const dragged = rect(204, 700, 80, 20);
  const result = computeSnap({ dragged, siblings: [sibling], canvas: CANVAS });

  assert.equal(result.left, 200);
  assert.equal(result.guides.some((g) => g.axis === "x" && g.position === 200 && g.source === "sibling"), true);
});

test("computeSnap snaps the dragged element's centre-y to a sibling's centre-y (CLIF-CS-03-b)", () => {
  const sibling = rect(0, 300, 50, 100); // centreY = 350
  // dragged centreY = 344 + 5 = 349, 1px from 350 (within tolerance); x kept far from any candidate
  const dragged = rect(900, 344, 40, 10);
  const result = computeSnap({ dragged, siblings: [sibling], canvas: CANVAS });

  assert.equal(result.top, 345); // centreY becomes exactly 350
  assert.equal(result.guides.some((g) => g.axis === "y" && g.position === 350 && g.source === "sibling"), true);
});

test("computeSnap resolves two siblings offering candidates on different axes to exactly one winner per axis, deterministically (CLIF-CS-03-c)", () => {
  const siblingA = rect(100, 1000, 50, 20); // left edge = 100
  const siblingB = rect(900, 150, 60, 40); // centreY = 170
  // dragged.left = 104 (4px from siblingA.left = 100); dragged centreY = 166 + 4 = 170 (exact match)
  const dragged = rect(104, 166, 30, 8);

  const orderAB = computeSnap({ dragged, siblings: [siblingA, siblingB], canvas: CANVAS });
  const orderBA = computeSnap({ dragged, siblings: [siblingB, siblingA], canvas: CANVAS });

  assert.deepEqual(orderAB, orderBA); // deterministic regardless of sibling array order
  assert.equal(orderAB.guides.length, 2);
  assert.equal(orderAB.left, 100);
  assert.equal(orderAB.top, 166);

  const xGuides = orderAB.guides.filter((g) => g.axis === "x");
  const yGuides = orderAB.guides.filter((g) => g.axis === "y");
  assert.equal(xGuides.length, 1); // exactly one winner on x
  assert.equal(yGuides.length, 1); // exactly one winner on y
  assert.equal(xGuides[0]?.position, 100);
  assert.equal(yGuides[0]?.position, 170);
});

// ── CLIF-CS-04: tolerance boundary / free movement ──────────────────────────────

test("computeSnap snaps at exactly SNAP_TOLERANCE_PX (6px), the inclusive boundary", () => {
  // dragged centreX = 494 + 0 = 494; canvas centreX = 500; delta = 6, exactly at tolerance
  const dragged = rect(444, 0, 100, 10);
  const result = computeSnap({ dragged, siblings: [], canvas: CANVAS });

  assert.equal(result.left, 450);
  assert.equal(result.guides.length, 1);
});

test("computeSnap does not snap and returns unchanged position with no guides when nothing is within tolerance (CLIF-CS-04-a)", () => {
  // dragged centreX = 493 + 0 = 493; canvas centreX = 500; delta = 7, just outside tolerance
  const dragged = rect(443, 0, 100, 10);
  const result = computeSnap({ dragged, siblings: [], canvas: CANVAS });

  assert.equal(result.left, dragged.left);
  assert.equal(result.top, dragged.top);
  assert.deepEqual(result.guides, []);
});

test("computeSnap accepts a custom tolerance override", () => {
  const dragged = rect(0, 0, 20, 20); // centreX = 10, far from canvas centreX = 500 by default tolerance
  const result = computeSnap({ dragged, siblings: [], canvas: CANVAS, tolerance: 1000 });

  assert.equal(result.left, 480); // snapped: centreX 10 -> 500 with a huge tolerance window
});

// ── CLIF-CS-05: performance budget ──────────────────────────────────────────────

test(`computeSnap completes in < 1ms at SNAP_MAX_SIBLINGS (${SNAP_MAX_SIBLINGS}) siblings (CLIF-CS-05-a)`, () => {
  const siblings: Rect[] = Array.from({ length: SNAP_MAX_SIBLINGS }, (_, i) =>
    rect(i * 40, i * 30, 60, 40),
  );
  const dragged = rect(123, 456, 90, 45);

  // Warm up the JIT so the measured call reflects steady-state cost, not first-call compilation.
  for (let i = 0; i < 50; i++) computeSnap({ dragged, siblings, canvas: CANVAS });

  const start = performance.now();
  computeSnap({ dragged, siblings, canvas: CANVAS });
  const elapsedMs = performance.now() - start;

  assert.ok(elapsedMs < 1, `expected < 1ms, got ${elapsedMs}ms`);
});

// ── guideToOverlayStyle mapping ──────────────────────────────────────────────────

test("guideToOverlayStyle maps a vertical (x-axis) guide to a full-height 1px-wide overlay div, scaled", () => {
  const style = guideToOverlayStyle({ axis: "x", position: 500, from: 0, to: 800, source: "canvas" }, 0.5);
  assert.deepEqual(style, { left: 250, top: 0, width: 1, height: 400 });
});

test("guideToOverlayStyle maps a horizontal (y-axis) guide to a full-width 1px-tall overlay div, scaled", () => {
  const style = guideToOverlayStyle({ axis: "y", position: 400, from: 0, to: 1000, source: "sibling" }, 2);
  assert.deepEqual(style, { left: 0, top: 800, width: 2000, height: 1 });
});

test(`SNAP_TOLERANCE_PX is 6 and SNAP_MAX_SIBLINGS is ${SNAP_MAX_SIBLINGS}`, () => {
  assert.equal(SNAP_TOLERANCE_PX, 6);
  assert.equal(SNAP_MAX_SIBLINGS, 24);
});

// ── offsetBoxesToRects: sibling-rect-snapshot mapping (WU-8b) ───────────────────

test("offsetBoxesToRects maps offsetLeft/offsetTop/offsetWidth/offsetHeight to left/top/width/height, preserving order", () => {
  const boxes: OffsetBox[] = [
    { offsetLeft: 10, offsetTop: 20, offsetWidth: 30, offsetHeight: 40 },
    { offsetLeft: 1, offsetTop: 2, offsetWidth: 3, offsetHeight: 4 },
  ];

  assert.deepEqual(offsetBoxesToRects(boxes), [
    { left: 10, top: 20, width: 30, height: 40 },
    { left: 1, top: 2, width: 3, height: 4 },
  ]);
});

test("offsetBoxesToRects returns an empty array for an empty snapshot", () => {
  assert.deepEqual(offsetBoxesToRects([]), []);
});

test("offsetBoxesToRects output feeds computeSnap directly (integration smoke)", () => {
  const boxes: OffsetBox[] = [{ offsetLeft: 200, offsetTop: 500, offsetWidth: 120, offsetHeight: 60 }];
  const siblings = offsetBoxesToRects(boxes);
  const dragged = rect(204, 700, 80, 20); // 4px from sibling.left = 200, within tolerance
  const result = computeSnap({ dragged, siblings, canvas: CANVAS });

  assert.equal(result.left, 200);
});
