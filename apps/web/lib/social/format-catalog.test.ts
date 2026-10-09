import assert from "node:assert/strict";
import { test } from "node:test";
import { mapSelectableFormatOptions } from "./format-catalog";
import type { FormatCatalogEntry } from "@/lib/api/content-generation";

function entry(overrides: Partial<FormatCatalogEntry>): FormatCatalogEntry {
  return {
    formatKey: "feed-single",
    surface: "feed",
    layout: "single",
    medium: "image",
    displayLabel: "Feed Single",
    isMultiAsset: false,
    isAuxiliary: false,
    ...overrides
  };
}

test("mapSelectableFormatOptions maps deliverable entries to formatKey + label", () => {
  const entries = [
    entry({ formatKey: "reel-script", displayLabel: "Script" }),
    entry({ formatKey: "feed-carousel", displayLabel: "Feed · Carousel" })
  ];

  const options = mapSelectableFormatOptions(entries);

  assert.deepEqual(options, [
    { formatKey: "reel-script", label: "Script" },
    { formatKey: "feed-carousel", label: "Feed · Carousel" }
  ]);
});

test("mapSelectableFormatOptions defensively excludes auxiliary entries even if the backend leaks one", () => {
  const entries = [
    entry({ formatKey: "feed-single", displayLabel: "Feed Single", isAuxiliary: false }),
    entry({ formatKey: "caption", displayLabel: "caption", isAuxiliary: true }),
    entry({ formatKey: "hook", displayLabel: "hook", isAuxiliary: true }),
    entry({ formatKey: "content-idea", displayLabel: "content-idea", isAuxiliary: true }),
    entry({ formatKey: "cta-variation", displayLabel: "cta-variation", isAuxiliary: true })
  ];

  const options = mapSelectableFormatOptions(entries);

  assert.deepEqual(options, [{ formatKey: "feed-single", label: "Feed Single" }]);
});

test("mapSelectableFormatOptions with an empty catalog returns an empty list", () => {
  assert.deepEqual(mapSelectableFormatOptions([]), []);
});
