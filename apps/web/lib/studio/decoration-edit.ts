/**
 * Pure helpers for the content-studio decoration editor. Kept free of DOM/React so they can be
 * unit-tested with the project's `tsx --test` runner; the imperative canvas wiring calls into them.
 */

export type RecolorProperty = "color" | "borderColor" | "backgroundColor";

/**
 * Decides which CSS property a recolor should target for the selected element.
 *
 * - Text elements recolor their glyphs (`color`).
 * - Border-drawn decorations (thin lines, rings, dividers) recolor their `borderColor` — the visible
 *   mark is the border, so setting `backgroundColor` would be a no-op (this was the studio bug where
 *   "líneas finas" could not be recolored).
 * - Filled shapes with no text and no border recolor their `backgroundColor`.
 */
export function pickRecolorProperty(el: { hasText: boolean; hasVisibleBorder: boolean }): RecolorProperty {
  if (el.hasText) return "color";
  if (el.hasVisibleBorder) return "borderColor";
  return "backgroundColor";
}

/**
 * True when a computed border is actually visible (has a non-none style and a positive width),
 * so a border-only decoration is recolored on its edge rather than its (transparent) fill.
 */
export function hasVisibleBorder(borderStyle: string, borderWidthPx: number): boolean {
  return borderStyle !== "none" && borderStyle !== "" && borderWidthPx > 0;
}
