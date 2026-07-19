import { test } from "node:test";
import assert from "node:assert/strict";
import {
  isBackgroundLayer,
  isContentLayer,
  isDecorLayer,
  resolveTargetElement,
  pickElement,
  isInsideEditingChrome,
  ensureDecorLayer,
  upgradeLegacyShapes,
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
  children: FakeElement[] = [];
  textContent = "";
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

  hasAttribute(name: string): boolean {
    return Object.prototype.hasOwnProperty.call(this.attrs, name);
  }

  setAttribute(name: string, value: string): void {
    this.attrs[name] = value;
  }

  get nextSibling(): FakeElement | null {
    const siblings = this.parentElement?.children ?? [];
    const i = siblings.indexOf(this);
    return i >= 0 && i + 1 < siblings.length ? siblings[i + 1] : null;
  }

  appendChild<T extends FakeElement>(child: T): T {
    child.parentElement = this;
    this.children.push(child);
    return child;
  }

  insertBefore<T extends FakeElement>(child: T, reference: FakeElement | null): T {
    child.parentElement = this;
    const i = reference ? this.children.indexOf(reference) : -1;
    if (i >= 0) this.children.splice(i, 0, child);
    else this.children.push(child);
    return child;
  }

  querySelector(selector: string): FakeElement | null {
    return this.queryAll(selector)[0] ?? null;
  }

  querySelectorAll(selector: string): FakeElement[] {
    return this.queryAll(selector);
  }

  private queryAll(selector: string): FakeElement[] {
    const results: FakeElement[] = [];
    const walk = (node: FakeElement) => {
      for (const child of node.children) {
        if (matchesSelector(child, selector)) results.push(child);
        walk(child);
      }
    };
    walk(this);
    return results;
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
    if (matchesSelector(this, selector)) return this;
    return this.parentElement ? this.parentElement.closest(selector) : null;
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
  if (parent) parent.appendChild(node);
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

// ── Phase 5: allow-list pickElement, decor layer, typed shapes ─────────────────

function buildLayeredFixture() {
  const root = el(null, {}, ["composition"]);
  const background = el(root, { "data-layer": "background" });
  const decor = el(root, { "data-layer": "decor" });
  const content = el(root, { "data-layer": "content" });
  const headline = el(content, { "data-slot": "headline" });
  const shape = el(decor, { "data-slot": "shape-1", "data-el-kind": "shape" });
  const legacyShape = el(background, {}); // pre-Phase-5 shape: no data-slot identity
  legacyShape.setAttribute("style", "position:absolute;left:10px;top:10px;width:100px;height:20px;");
  return { root, background, decor, content, headline, shape, legacyShape };
}

test("isDecorLayer recognizes data-layer=decor and the legacy class", () => {
  assert.equal(isDecorLayer(asElement(el(null, { "data-layer": "decor" }))), true);
  assert.equal(isDecorLayer(asElement(el(null, {}, ["layer-decor"]))), true);
  assert.equal(isDecorLayer(asElement(el(null, { "data-layer": "background" }))), false);
});

test("pickElement re-selects a shape by its stable data-slot identity, independent of which layer holds it", () => {
  const { root, shape } = buildLayeredFixture();
  assert.equal(pickElement(asNode(shape), asElement(root)), asElement(shape));
});

test("pickElement resolves a click on a nested child back to its selectable ancestor slot (allow-list closest walk)", () => {
  const { root, shape } = buildLayeredFixture();
  const glyph = el(shape, {}); // e.g. an icon/child node inside the shape
  assert.equal(pickElement(asNode(glyph), asElement(root)), asElement(shape));
});

test("pickElement does not select a bare decor-layer container, only its data-slot children", () => {
  const { root, decor } = buildLayeredFixture();
  assert.equal(pickElement(asNode(decor), asElement(root)), null);
});

test("pickElement falls back to the enclosing background layer for a legacy shape with no typed identity (R1.5: renders, not individually selectable until upgraded)", () => {
  const { root, legacyShape, background } = buildLayeredFixture();
  // No data-slot on the legacy shape itself, so the allow-list walk resolves to its nearest
  // selectable ancestor — the background layer it still physically lives inside — rather than the
  // shape itself. In practice upgradeLegacyShapes() always runs on mount before a click can occur,
  // so this intermediate state is transient; it must still degrade sanely (never null/crash) if hit.
  assert.equal(pickElement(asNode(legacyShape), asElement(root)), asElement(background));
});

test("pickElement still selects the text slot and background layer once a decor layer is present", () => {
  const { root, headline, background } = buildLayeredFixture();
  assert.equal(pickElement(asNode(headline), asElement(root)), asElement(headline));
  assert.equal(pickElement(asNode(background), asElement(root)), asElement(background));
});

test("ensureDecorLayer creates a decor layer directly after the background layer when none exists", () => {
  const root = el(null, {}, ["composition"]);
  const background = el(root, { "data-layer": "background" });
  el(root, { "data-layer": "content" });

  const decor = ensureDecorLayer(asElement(root), () => asElement(new FakeElement()));

  assert.equal(isDecorLayer(decor), true);
  const siblings = root.children;
  assert.equal(siblings.indexOf(background) + 1, siblings.findIndex((c) => isDecorLayer(asElement(c))));
});

test("ensureDecorLayer returns the existing decor layer instead of creating a duplicate", () => {
  const { root, decor } = buildLayeredFixture();
  const result = ensureDecorLayer(asElement(root), () => asElement(new FakeElement()));
  assert.equal(result, asElement(decor));
  assert.equal(root.children.filter((c) => isDecorLayer(asElement(c))).length, 1);
});

test("upgradeLegacyShapes stamps a data-slot + data-el-kind on untyped positioned shapes and reports a change", () => {
  const { root, legacyShape } = buildLayeredFixture();
  const upgraded = upgradeLegacyShapes(asElement(root) as unknown as ParentNode & HTMLElement, () => "shape-fixed-id");

  assert.equal(upgraded, true);
  assert.equal(legacyShape.getAttribute("data-slot"), "shape-fixed-id");
  assert.equal(legacyShape.getAttribute("data-el-kind"), "shape");
});

test("upgradeLegacyShapes never touches an element that already carries text content", () => {
  const root = el(null, {}, ["composition"]);
  const background = el(root, { "data-layer": "background" });
  const textish = el(background, {});
  textish.setAttribute("style", "position:absolute;");
  textish.textContent = "Not actually a shape";

  const upgraded = upgradeLegacyShapes(asElement(root) as unknown as ParentNode & HTMLElement, () => "shape-x");

  assert.equal(upgraded, false);
  assert.equal(textish.getAttribute("data-slot"), null);
});

test("upgradeLegacyShapes is a no-op (returns false) once every shape already has an identity", () => {
  const { root } = buildLayeredFixture();
  upgradeLegacyShapes(asElement(root) as unknown as ParentNode & HTMLElement, () => "shape-once");
  const secondPass = upgradeLegacyShapes(asElement(root) as unknown as ParentNode & HTMLElement, () => "shape-twice");
  assert.equal(secondPass, false);
});
