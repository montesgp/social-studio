import { test } from "node:test";
import assert from "node:assert/strict";
import { canPublishItem, reviewActionsFor } from "./review-actions.ts";

test("canPublishItem requires BOTH an approved editorial state and a publishable format", () => {
  assert.equal(canPublishItem("Approved", true), true);
  assert.equal(canPublishItem("ReadyForPublication", true), true);

  // Draft/rejected pieces are not publishable regardless of format.
  assert.equal(canPublishItem("Draft", true), false);
  assert.equal(canPublishItem("Rejected", true), false);
});

test("canPublishItem refuses script/copy deliverables even once approved", () => {
  // reel-script reaches Approved like any other piece, but there is no network API to post it to.
  assert.equal(canPublishItem("Approved", false), false);
  assert.equal(canPublishItem("ReadyForPublication", false), false);
});

test("reviewActionsFor offers approve/reject on an open item", () => {
  const actions = reviewActionsFor("Draft");
  assert.equal(actions.canApprove, true);
  assert.equal(actions.canReject, true);
  assert.equal(actions.canStage, false);
  assert.equal(actions.isTerminal, false);
});

test("reviewActionsFor only allows staging straight from Approved", () => {
  assert.equal(reviewActionsFor("Approved").canStage, true);
  assert.equal(reviewActionsFor("Draft").canStage, false);
  assert.equal(reviewActionsFor("Rejected").canStage, false);
});

test("reviewActionsFor offers nothing further on terminal states", () => {
  for (const state of ["Discarded", "ReadyForPublication", "HandedOff"] as const) {
    const actions = reviewActionsFor(state);
    assert.equal(actions.isTerminal, true, `${state} should be terminal`);
    assert.equal(actions.canApprove, false);
    assert.equal(actions.canReject, false);
  }
});

test("reviewActionsFor does not re-offer the state an item is already in", () => {
  assert.equal(reviewActionsFor("Approved").canApprove, false);
  assert.equal(reviewActionsFor("Rejected").canReject, false);
});
