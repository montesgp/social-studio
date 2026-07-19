import { test } from "node:test";
import assert from "node:assert/strict";
import {
  isBackgroundLayer,
  isContentLayer,
  resolveTargetElement,
  pickElement,
  isInsideEditingChrome,
} from "./canvas-hit-test.ts";

/**
 * Characterization tests (Phase 4) for the design canvas's hit-testing helpers, written BEFORE the
 * layer/typed-shape refactor (Phase 5) so they capture the CURRENT, still-working text-slot editing
 * behavior. This is the safety net: it must stay green through the Phase 5 rewrite of `pickElement`'s
 * internals (deny-list -> allow-list), because these are the same fixtures a real click resolves to.
 *
 * No jsdom/browser is available in this repo's test runner (`tsx --test`, plain Node, no DOM globals).
 * These helpers are extracted into this module (matching the project's existing `toolbar-dock.ts` /
 * `decoration-edit.ts` convention: DOM-adjacent pure logic lives in `lib/studio/*.ts`) so they can be
 * exercised against minimal fake DOM objects, cast to the real DOM types the production code uses.
 */

class FakeElement {
  readonly nodeType = 1;
  parentElement: FakeElement | null = null;
  private readonly attrs: Record<string, string>;
  private readonly classes: Set<string>;

  constructor(attrs: Record<string, string> = {}, classNames: string[] = []) {
    this.attrs = attrs;
    this.classes = new Set(classNames);
  }

  get classList() {
    return { contains: (c: string) => this.classes.has(c) };
  }

  getAttribute(name: string): string | null {
    return Object.prototype.hasOwnProperty.call(this.attrs, name) ? this.attrs[name] : null;
  }

  contains(other: FakeElement | FakeTextNode | null): boolean {
    let node: FakeElement | null = other instanceof FakeTextNode ? other.parentElement : other;
    while (node) {
      if (node === this) return true;
      node = node.parentElement;
    }
    return false;
  }

  closest(selector: string): FakeElement | null {
    let node: FakeElement | null = this;
    while (node) {
      if (matchesSelector(node, selector)) return node;
      node = node.parentElement;
    }
    return null;
  }
}

class FakeTextNode {
  readonly nodeType = 3;
  constructor(public parentElement: FakeElement | null) {}
}

function matchesSelector(el: FakeElement, selector: string): boolean {
  return selector.split(",").some((raw) => {
    const sel = raw.trim();
    const m = sel.match(/^\[([a-zA-Z0-9-]+)(="([^"]*)")?\]$/);
    if (!m) return false;
    const [, attr, , value] = m;
    return value === undefined ? el.getAttribute(attr) !== null : el.getAttribute(attr) === value;
  });
}

function el(parent: FakeElement | null, attrs: Record<string, string> = {}, classNames: string[] = []): FakeElement {
  const node = new FakeElement(attrs, classNames);
  node.parentElement = parent;
  return node;
}

function asElement(node: FakeElement): HTMLElement {
  return node as unknown as HTMLElement;
}
function asNode(node: FakeElement | FakeTextNode): Node {
  return node as unknown as Node;
}

// ── Fixture tree, mirroring the real design markup ─────────────────────────────
// root (.composition)
//  ├─ background ([data-layer="background"])
//  └─ content ([data-layer="content"])
//      └─ headline ([data-slot="headline"])
//          └─ (text node)
function buildFixture() {
  const root = el(null, {}, ["composition"]);
  const background = el(root, { "data-layer": "background" });
  const content = el(root, { "data-layer": "content" });
  const headline = el(content, { "data-slot": "headline" });
  const textNode = new FakeTextNode(headline);
  const outsider = el(null); // not under root at all
  return { root, background, content, headline, textNode, outsider };
}

test("isBackgroundLayer recognizes data-layer=background and the legacy class", () => {
  assert.equal(isBackgroundLayer(asElement(el(null, { "data-layer": "background" }))), true);
  assert.equal(isBackgroundLayer(asElement(el(null, {}, ["layer-background"]))), true);
  assert.equal(isBackgroundLayer(asElement(el(null, { "data-layer": "content" }))), false);
});

test("isContentLayer recognizes data-layer=content and the legacy class", () => {
  assert.equal(isContentLayer(asElement(el(null, { "data-layer": "content" }))), true);
  assert.equal(isContentLayer(asElement(el(null, {}, ["layer-content"]))), true);
  assert.equal(isContentLayer(asElement(el(null, { "data-layer": "background" }))), false);
});

test("resolveTargetElement returns element nodes as-is", () => {
  const { headline } = buildFixture();
  assert.equal(resolveTargetElement(asNode(headline)), asElement(headline));
});

test("resolveTargetElement resolves a text node to its parent element", () => {
  const { headline, textNode } = buildFixture();
  assert.equal(resolveTargetElement(asNode(textNode)), asElement(headline));
});

test("pickElement selects a data-slot text element directly", () => {
  const { root, headline } = buildFixture();
  assert.equal(pickElement(asNode(headline), asElement(root)), asElement(headline));
});

test("pickElement resolves a text-node click target to its selectable parent slot", () => {
  const { root, headline, textNode } = buildFixture();
  assert.equal(pickElement(asNode(textNode), asElement(root)), asElement(headline));
});

test("pickElement selects the background art layer (recolorable, but caller must suppress move/resize)", () => {
  const { root, background } = buildFixture();
  assert.equal(pickElement(asNode(background), asElement(root)), asElement(background));
});

test("pickElement rejects the content-layer wrapper itself (pure layout wrapper, never selectable)", () => {
  const { root, content } = buildFixture();
  assert.equal(pickElement(asNode(content), asElement(root)), null);
});

test("pickElement rejects the composition root itself", () => {
  const { root } = buildFixture();
  assert.equal(pickElement(asNode(root), asElement(root)), null);
});

test("pickElement rejects a target outside the root subtree", () => {
  const { root, outsider } = buildFixture();
  assert.equal(pickElement(asNode(outsider), asElement(root)), null);
});

test("isInsideEditingChrome is true for canvas/toolbar/handle descendants", () => {
  const canvas = el(null, { "data-cs-canvas": "" });
  const insideCanvas = el(canvas);
  const toolbar = el(null, { "data-cs-toolbar": "" });
  const handle = el(null, { "data-cs-handle": "" });
  assert.equal(isInsideEditingChrome(asElement(insideCanvas)), true);
  assert.equal(isInsideEditingChrome(asElement(toolbar)), true);
  assert.equal(isInsideEditingChrome(asElement(handle)), true);
});

test("isInsideEditingChrome is false for unrelated app UI", () => {
  const sidebarButton = el(null, { "data-testid": "approve-button" });
  assert.equal(isInsideEditingChrome(asElement(sidebarButton)), false);
});
