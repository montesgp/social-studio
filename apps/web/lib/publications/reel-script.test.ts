import { test } from "node:test";
import assert from "node:assert/strict";
import { scriptPreviewLines, textPreviewEntries, textPreviewLines, tryParseReelScript } from "./reel-script.ts";

test("tryParseReelScript returns null for plain prose", () => {
  assert.equal(tryParseReelScript("just some copy"), null);
});

test("tryParseReelScript returns null for JSON without scenes", () => {
  // A caption object is valid JSON but is not a script — rendering it as one would be wrong.
  assert.equal(tryParseReelScript('{"hook":"no scenes here"}'), null);
});

test("tryParseReelScript parses a scene-bearing script", () => {
  const script = tryParseReelScript('{"hook":"Hi","scenes":[{"timestamp":"0:00","voiceover":"Open"}]}');
  assert.notEqual(script, null);
  assert.equal(script?.hook, "Hi");
  assert.equal(script?.scenes?.length, 1);
});

test("scriptPreviewLines flattens hook, scenes and CTA in order", () => {
  const lines = scriptPreviewLines({
    hook: "The hook",
    scenes: [
      { timestamp: "0:00", voiceover: "First cue" },
      { timestamp: "0:05", onScreenText: "Second cue" },
    ],
    ctaText: "Book a call",
  });

  assert.deepEqual(lines, ["The hook", "0:00 First cue", "0:05 Second cue", "Book a call"]);
});

test("scriptPreviewLines skips scenes with no cue and falls back to an index label", () => {
  const lines = scriptPreviewLines({ scenes: [{}, { voiceover: "Only cue" }] });
  assert.deepEqual(lines, ["#2 Only cue"]);
});

test("textPreviewLines renders raw prose when the body is not a script", () => {
  assert.deepEqual(textPreviewLines("line one\n\n  line two  "), ["line one", "line two"]);
});

test("textPreviewLines prefers the structured script when the body is one", () => {
  // The reel-script body is stored as JSON; showing a raw JSON slice told the operator nothing.
  const lines = textPreviewLines('{"hook":"Hooked","scenes":[{"timestamp":"0:00","voiceover":"Go"}]}');
  assert.deepEqual(lines, ["Hooked", "0:00 Go"]);
});

test("textPreviewEntries keeps the timestamp separate so it can be styled", () => {
  const entries = textPreviewEntries(
    '{"hook":"Hooked","scenes":[{"timestamp":"0:00–0:03","voiceover":"Go"}],"ctaText":"Follow"}',
  );
  assert.deepEqual(entries, [
    { cue: null, text: "Hooked" },
    { cue: "0:00–0:03", text: "Go" },
    { cue: null, text: "Follow" },
  ]);
});

test("textPreviewEntries falls back to plain prose lines with no cue", () => {
  assert.deepEqual(textPreviewEntries("line one\n\n  line two  "), [
    { cue: null, text: "line one" },
    { cue: null, text: "line two" },
  ]);
});

test("textPreviewEntries numbers a scene that carries no timestamp", () => {
  const entries = textPreviewEntries('{"scenes":[{"voiceover":"No timestamp here"}]}');
  assert.deepEqual(entries, [{ cue: "#1", text: "No timestamp here" }]);
});
