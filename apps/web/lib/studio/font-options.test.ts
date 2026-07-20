import { test } from "node:test";
import assert from "node:assert/strict";
import { groupFontOptions } from "./font-options.ts";

test("groupFontOptions: splits brand entries from system entries, preserving order", () => {
  const grouped = groupFontOptions([
    { family: "Poppins", isBrand: true },
    { family: "Poppins Bold", isBrand: true },
    { family: "Liberation Sans", isBrand: false },
    { family: "DejaVu Sans", isBrand: false },
  ]);

  assert.deepEqual(grouped.brand.map((f) => f.family), ["Poppins", "Poppins Bold"]);
  assert.deepEqual(grouped.system.map((f) => f.family), ["Liberation Sans", "DejaVu Sans"]);
});

test("groupFontOptions: empty brand typography yields system-only group (R2.2)", () => {
  const grouped = groupFontOptions([
    { family: "Liberation Sans", isBrand: false },
    { family: "Liberation Serif", isBrand: false },
  ]);

  assert.equal(grouped.brand.length, 0);
  assert.equal(grouped.system.length, 2);
});

test("groupFontOptions: empty catalog yields two empty groups", () => {
  const grouped = groupFontOptions([]);

  assert.deepEqual(grouped, { brand: [], system: [] });
});
