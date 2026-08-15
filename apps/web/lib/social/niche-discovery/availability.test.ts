import { test } from "node:test";
import assert from "node:assert/strict";

import { formatAvailableNumber, isAvailable } from "./availability";

// ND-05 / design §11 — the frontend zero-fill trap's last line of defence: a metric the network
// does not report must never render the same as a real zero.

test("null is rendered as unavailable, never as 0", () => {
  assert.equal(formatAvailableNumber(null), "unavailable");
});

test("undefined is rendered as unavailable", () => {
  assert.equal(formatAvailableNumber(undefined), "unavailable");
});

test("a reported zero is rendered as 0, distinct from unavailable", () => {
  assert.equal(formatAvailableNumber(0), "0");
});

test("a reported positive number is rendered with locale grouping", () => {
  assert.equal(formatAvailableNumber(12345), "12,345");
});

test("isAvailable is false only for null/undefined, true for 0", () => {
  assert.equal(isAvailable(null), false);
  assert.equal(isAvailable(undefined), false);
  assert.equal(isAvailable(0), true);
  assert.equal(isAvailable(7), true);
});
