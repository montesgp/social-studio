/**
 * Pure DOM-hit-testing helpers for the content-studio design canvas. Kept free of DOM/React so they
 * can be unit-tested with the project's `tsx --test` runner (no jsdom in this repo); the imperative
 * canvas wiring in `content-studio-panel.tsx`'s `DesignCanvas` calls into these with real DOM
 * `Node`/`HTMLElement` instances at runtime.
 *
 * Extracted from `DesignCanvas` (Phase 4, characterization) to make the current pointer/layer logic
 * testable BEFORE the Phase 5 layer/typed-shape rewrite touches it — this file's behavior at this
 * commit is a pure move, not a change.
 */

const EDITING_CHROME_SELECTOR = "[data-cs-canvas],[data-cs-toolbar],[data-cs-handle]";

/** The full-bleed art layer (`data-layer="background"`): selectable to recolor its fill, but never dragged. */
export function isBackgroundLayer(el: HTMLElement): boolean {
  return el.getAttribute("data-layer") === "background" || el.classList.contains("layer-background");
}

/** The text-only wrapper (`data-layer="content"`): a pure layout wrapper, never directly selectable. */
export function isContentLayer(el: HTMLElement): boolean {
  return el.getAttribute("data-layer") === "content" || el.classList.contains("layer-content");
}

/** Resolves a raw event-target node to the element it should be treated as (text nodes resolve to their parent). */
export function resolveTargetElement(node: Node): HTMLElement | null {
  if (node.nodeType === 1) return node as HTMLElement;
  return node.parentElement as HTMLElement | null;
}

/**
 * Picks the editable element under a click. Text slots and decorative shapes are draggable/editable;
 * the background layer is selectable too — so its base fill can be recolored — but the pointer handler
 * never lets it move or resize. Only the structural wrappers (composition root, content layer) are inert.
 */
export function pickElement(node: Node, root: HTMLElement): HTMLElement | null {
  const el = resolveTargetElement(node);
  if (!el || !root.contains(el) || el === root) return null;
  if (el.classList.contains("composition") || isContentLayer(el)) return null;
  return el;
}

/** True when the target sits inside the canvas surface, the floating toolbar, or a resize handle. */
export function isInsideEditingChrome(target: HTMLElement): boolean {
  return target.closest(EDITING_CHROME_SELECTOR) !== null;
}
