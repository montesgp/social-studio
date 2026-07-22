import { test } from "node:test";
import assert from "node:assert/strict";
import { resolveBankThumbnail } from "./thumbnail.ts";

test("resolveBankThumbnail: returns null for null markup", () => {
  assert.equal(resolveBankThumbnail(null), null);
});

test("resolveBankThumbnail: returns null for invalid JSON", () => {
  assert.equal(resolveBankThumbnail("not json"), null);
});

test("resolveBankThumbnail: prefers flattenedImageRef when present", () => {
  const markup = JSON.stringify({ flattenedImageRef: "data:image/png;base64,AAAA", markup: "<div></div>" });
  assert.equal(resolveBankThumbnail(markup), "data:image/png;base64,AAAA");
});

test("resolveBankThumbnail: falls back to the first resolved <img src> in markup (data URI)", () => {
  const markup = JSON.stringify({
    markup: '<div class="composition"><img src="data:image/png;base64,BBBB" /></div>',
  });
  assert.equal(resolveBankThumbnail(markup), "data:image/png;base64,BBBB");
});

test("resolveBankThumbnail: falls back to the first resolved <img src> in markup (https URL)", () => {
  const markup = JSON.stringify({
    markup: '<div><img src="https://cdn.example.com/slide.png" /></div>',
  });
  assert.equal(resolveBankThumbnail(markup), "https://cdn.example.com/slide.png");
});

test("resolveBankThumbnail: ignores an unresolved storage-path <img src>", () => {
  const markup = JSON.stringify({
    markup: '<div><img src="org/client/analysis/slide_1_bg.png" /></div>',
  });
  assert.equal(resolveBankThumbnail(markup), null);
});

test("resolveBankThumbnail: reads the first envelope when markup is an array", () => {
  const markup = JSON.stringify([
    { flattenedImageRef: "data:image/png;base64,CCCC", markup: "<div></div>" },
    { flattenedImageRef: "data:image/png;base64,DDDD", markup: "<div></div>" },
  ]);
  assert.equal(resolveBankThumbnail(markup), "data:image/png;base64,CCCC");
});

test("resolveBankThumbnail: returns null when no image reference is resolvable", () => {
  const markup = JSON.stringify({ markup: "<div>No image here</div>" });
  assert.equal(resolveBankThumbnail(markup), null);
});
