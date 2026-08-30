import { test } from "node:test";
import assert from "node:assert/strict";

import { buildScorePair } from "./score-pair";

// ND-10 / design §11 — opportunity and confidence MUST always render as two distinct labelled
// values, never averaged or combined into one number. No component-test harness exists in this
// repo yet (only `lib/**/*.test.ts` is wired into the frontend test command), so this pure
// decision function is what `score-pair-badge.tsx` renders directly and unconditionally — there
// is no code path inside it that could combine the two scores.

test("builds two distinct labelled entries for a 94/37 fixture", () => {
  const pair = buildScorePair(94, 37);
  assert.deepEqual(pair, [
    { label: "Opportunity", value: 94 },
    { label: "Confidence", value: 37 },
  ]);
});

test("builds two distinct labelled entries for an 88/95 fixture, never averaged", () => {
  const pair = buildScorePair(88, 95);
  assert.deepEqual(pair, [
    { label: "Opportunity", value: 88 },
    { label: "Confidence", value: 95 },
  ]);
  const average = (88 + 95) / 2;
  assert.notEqual(pair[0].value, average);
  assert.notEqual(pair[1].value, average);
});

test("the two entries never have the same label", () => {
  const pair = buildScorePair(10, 10);
  assert.notEqual(pair[0].label, pair[1].label);
});
