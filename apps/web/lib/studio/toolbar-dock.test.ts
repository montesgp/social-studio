import { test } from "node:test";
import assert from "node:assert/strict";
import { resolveToolbarDock, TOOLBAR_COLUMN_WIDTH, TOOLBAR_GAP } from "./toolbar-dock.ts";

test("toolbar docks to the right of the slide render when there is room", () => {
  // Slide occupies 400..800 in a 1440px viewport — plenty of room on the right.
  const dock = resolveToolbarDock({ top: 200, left: 400, right: 800 }, 1440);

  assert.equal(dock.side, "right");
  assert.equal(dock.x, 800 + TOOLBAR_GAP);
});

test("toolbar never overlaps the slide it docks beside", () => {
  const canvas = { top: 200, left: 400, right: 800 };
  const dock = resolveToolbarDock(canvas, 1440);

  assert.ok(dock.x >= canvas.right, "column must start at or after the slide's right edge");
});

test("toolbar falls back to the left when the right side would overflow the viewport", () => {
  // Slide ends at 980 in a 1024px viewport: 980 + 12 + 44 > 1024 - 12, so the right dock overflows.
  const dock = resolveToolbarDock({ top: 200, left: 580, right: 980 }, 1024);

  assert.equal(dock.side, "left");
  assert.equal(dock.x, 580 - TOOLBAR_COLUMN_WIDTH - TOOLBAR_GAP);
});

test("left fallback is clamped to the viewport edge on very narrow screens", () => {
  // Slide starts at 8px — the left dock would compute a negative x.
  const dock = resolveToolbarDock({ top: 200, left: 8, right: 380 }, 390);

  assert.equal(dock.side, "left");
  assert.equal(dock.x, TOOLBAR_GAP);
  assert.ok(dock.x > 0, "column must stay on screen");
});

test("right dock is taken at the exact fit boundary", () => {
  // right = 900 + 12 = 912; 912 + 44 = 956 === 968 - 12 → fits exactly.
  const dock = resolveToolbarDock({ top: 200, left: 500, right: 900 }, 968);

  assert.equal(dock.side, "right");
});

test("toolbar top is clamped so it never docks under the app header", () => {
  assert.equal(resolveToolbarDock({ top: 0, left: 400, right: 800 }, 1440).y, 64);
  assert.equal(resolveToolbarDock({ top: -120, left: 400, right: 800 }, 1440).y, 64);
  // Below the clamp the slide's own top wins.
  assert.equal(resolveToolbarDock({ top: 300, left: 400, right: 800 }, 1440).y, 300);
});
