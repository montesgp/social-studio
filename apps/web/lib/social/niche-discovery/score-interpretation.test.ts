import { test } from "node:test";
import assert from "node:assert/strict";

import { NOT_COMPARABLE_VALUE, interpretRunScores } from "./score-interpretation";

// Every signal is normalized min-max ACROSS THE CLUSTERS OF ONE RUN
// (AvailabilityWeightedAggregate.NormalizeWithinSet). So OpportunityScore ranks within the run; it is
// not an absolute probability that a niche will work. These tests pin the wording that says so,
// because a score read as a probability is a score that sends the operator to the wrong market.

test("always states that scores rank within the run", () => {
  const interpretation = interpretRunScores(12);

  assert.match(interpretation.headline, /within this run/i);
  assert.ok(!/probabilit/i.test(interpretation.headline) || /not/i.test(interpretation.headline));
});

test("a multi-opportunity run carries no comparability caveat", () => {
  assert.equal(interpretRunScores(12).caveat, null);
  assert.equal(interpretRunScores(2).caveat, null);
});

// NormalizeWithinSet returns 0.5 for every entry when the set holds a single value, and Score returns
// 0.5 when no signal is available. That 0.5 reads like "average" and means "not comparable".
test("a single-opportunity run warns that 0.5 means not comparable", () => {
  const interpretation = interpretRunScores(1);

  assert.ok(interpretation.caveat);
  assert.match(interpretation.caveat!, /0\.5/);
  assert.match(interpretation.caveat!, /not comparable|cannot be ranked/i);
});

test("an empty run warns as well rather than implying a valid ranking", () => {
  assert.ok(interpretRunScores(0).caveat);
});

test("exposes the not-comparable value so the UI never hardcodes it", () => {
  assert.equal(NOT_COMPARABLE_VALUE, 0.5);
});
