import { test } from "node:test";
import assert from "node:assert/strict";
import { insertTextAtCaret } from "./text-insertion.ts";

test("insertTextAtCaret: inserts at the caret, keeping surrounding text", () => {
  const result = insertTextAtCaret("Hello world", "👋 ", 6);

  assert.equal(result.text, "Hello 👋 world");
  assert.equal(result.caretIndex, 6 + "👋 ".length);
});

test("insertTextAtCaret: inserts at the end when caret is at text length", () => {
  const result = insertTextAtCaret("Caption", " 🎉", 7);

  assert.equal(result.text, "Caption 🎉");
  assert.equal(result.caretIndex, 10);
});

test("insertTextAtCaret: inserts at the start when caretIndex is 0", () => {
  const result = insertTextAtCaret("world", "hello ", 0);

  assert.equal(result.text, "hello world");
  assert.equal(result.caretIndex, 6);
});

test("insertTextAtCaret: clamps an out-of-range caret to the text length", () => {
  const result = insertTextAtCaret("abc", "!", 999);

  assert.equal(result.text, "abc!");
  assert.equal(result.caretIndex, 4);
});
