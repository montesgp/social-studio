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

/** Selectable elements: text/shape slots (identity-bearing) and the recolorable background layer. */
const SELECTABLE_SELECTOR = '[data-slot], [data-layer="background"]';

/** The full-bleed art layer (`data-layer="background"`): selectable to recolor its fill, but never dragged. */
export function isBackgroundLayer(el: HTMLElement): boolean {
  return el.getAttribute("data-layer") === "background" || el.classList.contains("layer-background");
}

/** The decorative shapes layer (`data-layer="decor"`): sits above background, below content (Decision C1). */
export function isDecorLayer(el: HTMLElement): boolean {
  return el.getAttribute("data-layer") === "decor" || el.classList.contains("layer-decor");
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
 * Picks the editable element under a click, by ALLOW-list (Decision C2): the nearest ancestor (or
 * self) carrying `[data-slot]` — text slots and typed shapes alike, one identity mechanism for both —
 * or the recolorable `[data-layer="background"]` art layer. Structural wrappers (composition root,
 * content layer, decor layer container itself) become inert by NOT matching, rather than being
 * enumerated as a deny-list — so a future wrapper can't silently reintroduce the old bug where the
 * content layer's full-canvas hit area made a shape behind it permanently unselectable.
 */
export function pickElement(node: Node, root: HTMLElement): HTMLElement | null {
  const el = resolveTargetElement(node);
  if (!el || !root.contains(el) || el === root) return null;
  if (el.classList.contains("composition")) return null;
  const match = el.closest(SELECTABLE_SELECTOR) as HTMLElement | null;
  if (!match || !root.contains(match) || match === root) return null;
  return match;
}

/** True when the target sits inside the canvas surface, the floating toolbar, or a resize handle. */
export function isInsideEditingChrome(target: HTMLElement): boolean {
  return target.closest(EDITING_CHROME_SELECTOR) !== null;
}

/**
 * Ensures a `[data-layer="decor"]` container exists directly after the background layer (so paint
 * order is background -> decor -> content, per Decision C1) and returns it. Idempotent: returns the
 * existing decor layer instead of creating a duplicate on repeated calls (e.g. across markup reloads).
 */
export function ensureDecorLayer(
  root: HTMLElement,
  createElement: () => HTMLElement = () => document.createElement("div"),
): HTMLElement {
  const existing = root.querySelector<HTMLElement>('[data-layer="decor"]');
  if (existing) return existing;

  const decor = createElement();
  decor.setAttribute("data-layer", "decor");

  const background = root.querySelector<HTMLElement>('[data-layer="background"]');
  if (background?.parentElement) {
    background.parentElement.insertBefore(decor, background.nextSibling);
  } else {
    root.appendChild(decor);
  }

  return decor;
}

/**
 * Upgrades pre-Phase-5 shapes (positioned decorative elements with no `data-slot` identity, persisted
 * only as opaque markup — R1.5) to the typed model by stamping `data-slot` + `data-el-kind="shape"`.
 * Returns `true` when at least one element was upgraded, so the caller can mark the draft dirty only
 * when something actually changed (Decision B3).
 */
export function upgradeLegacyShapes(root: HTMLElement, makeShapeSlot: () => string = defaultShapeSlot): boolean {
  const layers = root.querySelectorAll<HTMLElement>('[data-layer="background"], [data-layer="decor"]');
  let upgraded = false;

  layers.forEach((layer) => {
    // `.children` (HTMLCollection) only ever contains element nodes — no text-node filtering needed.
    [...layer.children].forEach((child) => {
      if (isLegacyShapeCandidate(child)) {
        child.setAttribute("data-slot", makeShapeSlot());
        child.setAttribute("data-el-kind", "shape");
        upgraded = true;
      }
    });
  });

  return upgraded;
}

function isLegacyShapeCandidate(el: Element): boolean {
  if (el.hasAttribute("data-slot")) return false;
  if ((el.textContent ?? "").trim().length > 0) return false;
  return /position\s*:\s*absolute/i.test(el.getAttribute("style") ?? "");
}

function defaultShapeSlot(): string {
  return `shape-${crypto.randomUUID()}`;
}
