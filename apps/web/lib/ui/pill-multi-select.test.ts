import { test } from "node:test";
import assert from "node:assert/strict";
import {
  DEFAULT_SEARCH_DEBOUNCE_MS,
  addSelection,
  createDebouncedRunner,
  nextActiveIndex,
  normalizeSearchTerm,
  removeSelection,
  resolveListboxStatus,
  selectableOptions,
  type PillOption,
} from "./pill-multi-select.ts";

function option(value: string, label = value): PillOption {
  return { value, label };
}

// ── search term shaping ──────────────────────────────────────────────────────

test("normalizeSearchTerm: trims the operator's stray whitespace", () => {
  assert.equal(normalizeSearchTerm("  gastronomy  "), "gastronomy");
});

test("normalizeSearchTerm: collapses inner runs of whitespace to a single space", () => {
  assert.equal(normalizeSearchTerm("real    estate"), "real estate");
});

test("normalizeSearchTerm: whitespace-only input is the same as no term at all", () => {
  assert.equal(normalizeSearchTerm("   \t \n "), "");
});

// ── options the listbox may still offer ──────────────────────────────────────

test("selectableOptions: an already-selected option is never offered again", () => {
  const result = selectableOptions(
    [option("a"), option("b"), option("c")],
    [option("b")]
  );

  assert.deepEqual(
    result.map((o) => o.value),
    ["a", "c"]
  );
});

test("selectableOptions: preserves the order the caller's search returned", () => {
  const result = selectableOptions([option("z"), option("m"), option("a")], []);

  assert.deepEqual(
    result.map((o) => o.value),
    ["z", "m", "a"]
  );
});

test("selectableOptions: de-duplicates repeated values from the search callback", () => {
  const result = selectableOptions([option("a"), option("a"), option("b")], []);

  assert.deepEqual(
    result.map((o) => o.value),
    ["a", "b"]
  );
});

test("selectableOptions: everything selected leaves nothing to offer", () => {
  const result = selectableOptions([option("a"), option("b")], [option("a"), option("b")]);

  assert.deepEqual(result, []);
});

// ── what the listbox should say ──────────────────────────────────────────────

test("resolveListboxStatus: a request in flight reports loading, whatever else is true", () => {
  const status = resolveListboxStatus({
    isSearching: true,
    hasSearchTerm: true,
    fetchedCount: 0,
    selectableCount: 0,
  });

  assert.equal(status, "loading");
});

test("resolveListboxStatus: options available are reported as options", () => {
  const status = resolveListboxStatus({
    isSearching: false,
    hasSearchTerm: true,
    fetchedCount: 3,
    selectableCount: 2,
  });

  assert.equal(status, "options");
});

test("resolveListboxStatus: a search that matched nothing is a no-match, not an empty catalog", () => {
  const status = resolveListboxStatus({
    isSearching: false,
    hasSearchTerm: true,
    fetchedCount: 0,
    selectableCount: 0,
  });

  assert.equal(status, "no-matches");
});

test("resolveListboxStatus: matches that are all already picked say so instead of no-matches", () => {
  const status = resolveListboxStatus({
    isSearching: false,
    hasSearchTerm: true,
    fetchedCount: 2,
    selectableCount: 0,
  });

  assert.equal(status, "all-selected");
});

test("resolveListboxStatus: no term and no defaults means the catalog itself is empty", () => {
  const status = resolveListboxStatus({
    isSearching: false,
    hasSearchTerm: false,
    fetchedCount: 0,
    selectableCount: 0,
  });

  assert.equal(status, "empty-catalog");
});

test("resolveListboxStatus: no term with defaults all picked still reads as all-selected", () => {
  const status = resolveListboxStatus({
    isSearching: false,
    hasSearchTerm: false,
    fetchedCount: 5,
    selectableCount: 0,
  });

  assert.equal(status, "all-selected");
});

// ── selection transitions ────────────────────────────────────────────────────

test("addSelection: appends to the end so the operator's order is preserved", () => {
  const next = addSelection([option("a")], option("b"));

  assert.deepEqual(
    next.map((o) => o.value),
    ["a", "b"]
  );
});

test("addSelection: re-adding an existing value returns the same array reference", () => {
  const current = [option("a")];

  assert.equal(addSelection(current, option("a")), current);
});

test("addSelection: refuses to grow past the cap when one is given", () => {
  const current = [option("a"), option("b")];

  assert.equal(addSelection(current, option("c"), 2), current);
});

test("removeSelection: drops exactly the requested value", () => {
  const next = removeSelection([option("a"), option("b"), option("c")], "b");

  assert.deepEqual(
    next.map((o) => o.value),
    ["a", "c"]
  );
});

test("removeSelection: removing an absent value returns the same array reference", () => {
  const current = [option("a")];

  assert.equal(removeSelection(current, "zzz"), current);
});

// ── keyboard navigation ──────────────────────────────────────────────────────

test("nextActiveIndex: moving down from nothing active lands on the first option", () => {
  assert.equal(nextActiveIndex(-1, 3, 1), 0);
});

test("nextActiveIndex: moving up from nothing active lands on the last option", () => {
  assert.equal(nextActiveIndex(-1, 3, -1), 2);
});

test("nextActiveIndex: wraps around the bottom edge", () => {
  assert.equal(nextActiveIndex(2, 3, 1), 0);
});

test("nextActiveIndex: wraps around the top edge", () => {
  assert.equal(nextActiveIndex(0, 3, -1), 2);
});

test("nextActiveIndex: an empty list has no active option", () => {
  assert.equal(nextActiveIndex(0, 0, 1), -1);
});

test("nextActiveIndex: an index left over from a longer list is clamped back in range", () => {
  assert.equal(nextActiveIndex(9, 3, 1), 0);
});

// ── debounce shaping ─────────────────────────────────────────────────────────

test("DEFAULT_SEARCH_DEBOUNCE_MS: a keystroke does not fire a request per character", () => {
  assert.ok(DEFAULT_SEARCH_DEBOUNCE_MS >= 200);
});

test("createDebouncedRunner: a burst of calls runs the callback once, with the last value", async () => {
  const seen: string[] = [];
  const runner = createDebouncedRunner((term: string) => seen.push(term), 10);

  runner.run("g");
  runner.run("ga");
  runner.run("gas");

  await new Promise((resolve) => setTimeout(resolve, 40));

  assert.deepEqual(seen, ["gas"]);
});

test("createDebouncedRunner: cancel stops a pending call from ever running", async () => {
  const seen: string[] = [];
  const runner = createDebouncedRunner((term: string) => seen.push(term), 10);

  runner.run("gastronomy");
  runner.cancel();

  await new Promise((resolve) => setTimeout(resolve, 40));

  assert.deepEqual(seen, []);
});

test("createDebouncedRunner: flush runs the pending call immediately", () => {
  const seen: string[] = [];
  const runner = createDebouncedRunner((term: string) => seen.push(term), 10_000);

  runner.run("fitness");
  runner.flush();

  assert.deepEqual(seen, ["fitness"]);
});

test("createDebouncedRunner: flush with nothing pending is a no-op", () => {
  const seen: string[] = [];
  const runner = createDebouncedRunner((term: string) => seen.push(term), 10);

  runner.flush();

  assert.deepEqual(seen, []);
});

test("createDebouncedRunner: a zero delay still defers past the current keystroke", async () => {
  const seen: string[] = [];
  const runner = createDebouncedRunner((term: string) => seen.push(term), 0);

  runner.run("a");
  assert.deepEqual(seen, []);

  await new Promise((resolve) => setTimeout(resolve, 20));
  assert.deepEqual(seen, ["a"]);
});
