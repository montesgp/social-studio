/**
 * Font support for surfaces that RENDER text in a chosen family (Content Studio slides, brand-manual
 * previews).
 *
 * Picking a family is not enough to see it: unless the webfont is actually loaded into the document,
 * every unavailable family falls back to the SAME default, so successive picks look like nothing is
 * happening — the first pick appears to work only because it moves away from the original font.
 */

/** Families offered across the product. Order is the order they are presented in. */
export const COMMON_FONT_FAMILIES = [
  "Inter",
  "Roboto",
  "Open Sans",
  "Montserrat",
  "Poppins",
  "Lato",
  "Raleway",
  "Nunito",
  "Source Sans 3",
  "Merriweather",
  "Playfair Display",
  "Oswald",
  "Bebas Neue",
  "Arial",
  "Helvetica",
  "Georgia",
  "Times New Roman",
  "Verdana",
];

/** Always present on the host; never fetched as a webfont. */
export const SYSTEM_FONT_FAMILIES = new Set([
  "Arial",
  "Helvetica",
  "Georgia",
  "Times New Roman",
  "Verdana",
]);

const FONT_STACKS: Record<string, string> = {
  Inter: "Inter, ui-sans-serif, system-ui, sans-serif",
  Roboto: "Roboto, Arial, sans-serif",
  "Open Sans": "'Open Sans', Arial, sans-serif",
  Montserrat: "Montserrat, Arial, sans-serif",
  Poppins: "Poppins, Arial, sans-serif",
  Lato: "Lato, Arial, sans-serif",
  Raleway: "Raleway, Arial, sans-serif",
  Nunito: "Nunito, Arial, sans-serif",
  "Source Sans 3": "'Source Sans 3', Arial, sans-serif",
  Merriweather: "Merriweather, Georgia, serif",
  "Playfair Display": "'Playfair Display', Georgia, serif",
  Oswald: "Oswald, Arial, sans-serif",
  "Bebas Neue": "'Bebas Neue', Impact, sans-serif",
  Arial: "Arial, sans-serif",
  Helvetica: "Helvetica, Arial, sans-serif",
  Georgia: "Georgia, serif",
  "Times New Roman": "'Times New Roman', Times, serif",
  Verdana: "Verdana, Geneva, sans-serif",
};

/**
 * Browser equivalents for the families the SERVER renderer offers.
 *
 * `FontCatalog.SystemSafeList` is a list of OFL fonts chosen for the server-side renderer
 * (`Liberation Sans`, `DejaVu Serif`, …). None of them is served by Google Fonts and none is
 * normally installed on a user's machine, so a browser preview resolves every one of them to the
 * SAME generic fallback — which is exactly why picking different fonts appeared to do nothing.
 *
 * Each entry maps to a widely available face with comparable metrics, so the preview differentiates
 * the choices and approximates what the renderer will actually draw. Liberation Sans/Serif/Mono are
 * metric-compatible with Arial/Times/Courier by design; DejaVu descends from Vera, close to Verdana.
 */
const RENDERER_FONT_EQUIVALENTS: Record<string, string> = {
  "Liberation Sans": "'Liberation Sans', Arial, Helvetica, sans-serif",
  "Liberation Serif": "'Liberation Serif', 'Times New Roman', Times, serif",
  "Liberation Mono": "'Liberation Mono', 'Courier New', Courier, monospace",
  "DejaVu Sans": "'DejaVu Sans', Verdana, Geneva, sans-serif",
  "DejaVu Serif": "'DejaVu Serif', Georgia, 'Times New Roman', serif",
};

/** Families that resolve locally and must never be requested from a webfont CDN. */
export function isLocallyResolvableFont(family: string): boolean {
  return SYSTEM_FONT_FAMILIES.has(family) || family in RENDERER_FONT_EQUIVALENTS;
}

/**
 * A complete `font-family` value for a family name: the known stack, or the family quoted with a
 * generic fallback. Assigning the BARE name is what a naive picker does, and it leaves a multi-word
 * family (`Open Sans`) unquoted and gives the renderer no fallback at all.
 */
export function fontStackFor(family: string): string {
  return (
    RENDERER_FONT_EQUIVALENTS[family] ??
    FONT_STACKS[family] ??
    `'${family.replace(/'/g, "")}', sans-serif`
  );
}

/** The Google Fonts CSS2 URL for ONE family. */
export function webFontUrl(family: string): string {
  return `https://fonts.googleapis.com/css2?family=${family.trim().replace(/\s+/g, "+")}:wght@400;600;700&display=swap`;
}

/** Marks the single `<link>` that loads the server renderer's own font files. */
const RENDER_FONTS_LINK_ATTRIBUTE = "data-studio-render-fonts";

/**
 * Ensures the given families are available to the document (and therefore to any shadow root inside
 * it — `@font-face` is document-scoped, not tree-scoped).
 *
 * Families in `bundled` are embedded by the server renderer and served by the API through
 * `renderFontStylesheetUrl`: they load from there, once, so the editor draws the exact faces the
 * rendered slide uses. Only the remaining families are requested from Google Fonts.
 *
 * ONE `<link>` PER FAMILY, deliberately. The Google Fonts CSS2 endpoint answers 400 for the WHOLE
 * request when any single family is unknown to it, so batching meant one unrecognized brand font
 * silently stopped every other font from loading. Per-family links contain that blast radius to the
 * family that is actually missing. Locally resolvable families are never requested at all.
 *
 * Idempotent: a family already linked is skipped, so re-renders never re-fetch.
 */
export function ensureFontsLoaded(
  families: string[],
  bundled: ReadonlySet<string> = new Set(),
  renderFontStylesheetUrl?: string,
): void {
  if (typeof document === "undefined") {
    return;
  }

  if (bundled.size > 0 && renderFontStylesheetUrl && !document.head.querySelector(`link[${RENDER_FONTS_LINK_ATTRIBUTE}]`)) {
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.setAttribute(RENDER_FONTS_LINK_ATTRIBUTE, "");
    link.href = renderFontStylesheetUrl;
    document.head.appendChild(link);
  }

  for (const family of new Set(families.map((f) => f.trim()).filter(Boolean))) {
    if (isLocallyResolvableFont(family) || bundled.has(family)) {
      continue;
    }

    const selector = `link[data-studio-font="${CSS.escape(family)}"]`;
    if (document.head.querySelector(selector)) {
      continue;
    }

    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.dataset.studioFont = family;
    link.href = webFontUrl(family);
    document.head.appendChild(link);
  }
}
