/**
 * Pure helpers for docking the content-studio edit toolbar. Kept free of DOM/React so they can be
 * unit-tested with the project's `tsx --test` runner; the imperative canvas wiring calls into them.
 */

/**
 * Width (px) reserved for the vertical edit toolbar when deciding whether it fits beside the slide.
 * Covers the widest control (a 28px button) plus the panel's padding and border.
 */
export const TOOLBAR_COLUMN_WIDTH = 44;

/** Gap (px) kept between the toolbar column and the slide render (and the viewport edge). */
export const TOOLBAR_GAP = 12;

export interface DockRect {
  top: number;
  left: number;
  right: number;
}

export interface DockPosition {
  x: number;
  y: number;
  side: "right" | "left";
}

/**
 * Computes the default dock position for the vertical edit toolbar.
 *
 * The toolbar stacks its controls in a single column and docks to the RIGHT of the slide render, so
 * it never overlaps the component being edited. When the viewport has no room on the right (narrow
 * screens), it falls back to the matching gap on the left rather than hanging off-screen.
 *
 * `y` is clamped so the panel never docks above the app chrome.
 */
export function resolveToolbarDock(canvas: DockRect, viewportWidth: number): DockPosition {
  const right = Math.round(canvas.right + TOOLBAR_GAP);
  const fitsRight = right + TOOLBAR_COLUMN_WIDTH <= viewportWidth - TOOLBAR_GAP;

  if (fitsRight) {
    return { x: right, y: clampTop(canvas.top), side: "right" };
  }

  return {
    x: Math.max(TOOLBAR_GAP, Math.round(canvas.left - TOOLBAR_COLUMN_WIDTH - TOOLBAR_GAP)),
    y: clampTop(canvas.top),
    side: "left",
  };
}

/** Minimum viewport `y` so the toolbar never docks under/above the app header. */
const MIN_TOOLBAR_TOP = 64;

function clampTop(top: number): number {
  return Math.max(MIN_TOOLBAR_TOP, Math.round(top));
}
