import assert from "node:assert/strict";
import { test } from "node:test";
import { historyStatusBadgeVariant } from "./workflow-progress-landing";

test("historyStatusBadgeVariant is success for completed", () => {
  assert.equal(historyStatusBadgeVariant("completed"), "success");
});

test("historyStatusBadgeVariant is muted for any other status", () => {
  assert.equal(historyStatusBadgeVariant("in_progress"), "muted");
  assert.equal(historyStatusBadgeVariant("failed"), "muted");
  assert.equal(historyStatusBadgeVariant("unconfigured"), "muted");
});
