import { test } from "node:test";
import assert from "node:assert/strict";
import { pickRecolorProperty, hasVisibleBorder } from "./decoration-edit.ts";

test("recolor targets glyph color for text elements", () => {
  assert.equal(pickRecolorProperty({ hasText: true, hasVisibleBorder: false }), "color");
  // Text wins even when a border is present.
  assert.equal(pickRecolorProperty({ hasText: true, hasVisibleBorder: true }), "color");
});

test("recolor targets borderColor for border-only decorations (thin lines/rings)", () => {
  assert.equal(pickRecolorProperty({ hasText: false, hasVisibleBorder: true }), "borderColor");
});

test("recolor targets backgroundColor for filled shapes without text or border", () => {
  assert.equal(pickRecolorProperty({ hasText: false, hasVisibleBorder: false }), "backgroundColor");
});

test("hasVisibleBorder requires a real style and positive width", () => {
  assert.equal(hasVisibleBorder("solid", 1), true);
  assert.equal(hasVisibleBorder("none", 3), false);
  assert.equal(hasVisibleBorder("", 3), false);
  assert.equal(hasVisibleBorder("solid", 0), false);
});
