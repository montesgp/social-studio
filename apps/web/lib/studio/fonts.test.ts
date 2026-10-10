import { test } from "node:test";
import assert from "node:assert/strict";
import {
  COMMON_FONT_FAMILIES,
  SYSTEM_FONT_FAMILIES,
  fontStackFor,
  isLocallyResolvableFont,
  webFontUrl,
  ensureFontsLoaded,
} from "./fonts.ts";

/** Exactly what `FontCatalog.SystemSafeList` returns from the API. */
const RENDERER_FAMILIES = [
  "Liberation Sans",
  "Liberation Serif",
  "Liberation Mono",
  "DejaVu Sans",
  "DejaVu Serif",
];

test("fontStackFor always returns a stack with a fallback, never a bare family", () => {
  for (const family of [...COMMON_FONT_FAMILIES, ...RENDERER_FAMILIES]) {
    const stack = fontStackFor(family);
    assert.ok(stack.includes(","), `${family} resolved to a bare name: ${stack}`);
  }
});

test("each renderer font maps to a DISTINCT browser fallback", () => {
  // The bug this guards: every one of these resolved to the same generic fallback, so switching
  // between them changed nothing on screen and the picker looked broken.
  const fallbacks = RENDERER_FAMILIES.map((family) => {
    const tokens = fontStackFor(family).split(",").map((t) => t.trim());
    return tokens[1]; // the first token is the family itself, which is not installed
  });

  assert.equal(new Set(fallbacks).size, fallbacks.length, `fallbacks collide: ${fallbacks.join(" | ")}`);
});

test("renderer fonts keep their own family first so an installed copy still wins", () => {
  assert.ok(fontStackFor("Liberation Sans").startsWith("'Liberation Sans'"));
  assert.ok(fontStackFor("DejaVu Serif").startsWith("'DejaVu Serif'"));
});

test("fontStackFor quotes a multi-word family so the CSS value stays valid", () => {
  assert.equal(fontStackFor("Open Sans"), "'Open Sans', Arial, sans-serif");
});

test("fontStackFor handles an unknown brand family safely", () => {
  assert.equal(fontStackFor("Acme Grotesk"), "'Acme Grotesk', sans-serif");
  // A stray quote would otherwise break out of the CSS string.
  assert.equal(fontStackFor("Bad'Name"), "'BadName', sans-serif");
});

test("locally resolvable fonts are never requested from the webfont CDN", () => {
  for (const family of [...RENDERER_FAMILIES, ...SYSTEM_FONT_FAMILIES]) {
    assert.equal(isLocallyResolvableFont(family), true, `${family} would be fetched needlessly`);
  }
  assert.equal(isLocallyResolvableFont("Montserrat"), false);
});

test("webFontUrl requests ONE family per stylesheet", () => {
  // Batching is what made a single unknown brand font 400 the whole request and silently stop every
  // other font from loading.
  const url = webFontUrl("Open Sans");
  assert.equal(url.split("family=").length - 1, 1);
  assert.ok(url.includes("family=Open+Sans"));
});

/** Minimal `document` stand-in: the repo runs these tests without jsdom. */
type FakeLink = { rel?: string; href?: string; dataset: { studioFont?: string }; attributes: Set<string> };

function withFakeDocument(run: (links: FakeLink[]) => void): void {
  const links: FakeLink[] = [];
  const fake = {
    head: {
      querySelector: (selector: string) =>
        links.find((l) =>
          selector.includes("data-studio-render-fonts")
            ? l.attributes.has("data-studio-render-fonts")
            : l.dataset.studioFont !== undefined && selector.includes(`"${l.dataset.studioFont}"`),
        ) ?? null,
      appendChild: (link: FakeLink) => links.push(link),
    },
    createElement: (): FakeLink & { setAttribute: (name: string) => void } => {
      const link: FakeLink = { dataset: {}, attributes: new Set() };
      return Object.assign(link, { setAttribute: (name: string) => link.attributes.add(name) });
    },
  };
  const globals = globalThis as Record<string, unknown>;
  const previousDocument = globals.document;
  const previousCss = globals.CSS;
  globals.document = fake;
  globals.CSS = { escape: (value: string) => value };
  try {
    run(links);
  } finally {
    globals.document = previousDocument;
    globals.CSS = previousCss;
  }
}

test("bundled families load from the render-font stylesheet once, never from the CDN", () => {
  withFakeDocument((links) => {
    const bundled = new Set(["League Gothic", "Poppins"]);
    ensureFontsLoaded(["League Gothic", "Poppins", "Source Sans 3"], bundled, "/hive-api/fonts.css");
    ensureFontsLoaded(["League Gothic"], bundled, "/hive-api/fonts.css");

    const renderLinks = links.filter((l) => l.attributes.has("data-studio-render-fonts"));
    const cdnLinks = links.filter((l) => l.href?.startsWith("https://fonts.googleapis.com"));
    assert.equal(renderLinks.length, 1);
    assert.equal(renderLinks[0].href, "/hive-api/fonts.css");
    assert.deepEqual(cdnLinks.map((l) => l.href), [webFontUrl("Source Sans 3")]);
  });
});
