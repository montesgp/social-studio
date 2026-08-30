import assert from "node:assert/strict";
import { test } from "node:test";
import {
  CANDIDATE_STATUS_CHECK_BACK_MESSAGE,
  describeCandidateStatus
} from "./workflow-candidate-status";

test("describeCandidateStatus maps Queued to a pending, non-terminal presentation", () => {
  const result = describeCandidateStatus("Queued");
  assert.equal(result.label, "Queued");
  assert.equal(result.tone, "pending");
  assert.equal(result.isTerminal, false);
});

test("describeCandidateStatus maps Analyzing to a pending, non-terminal presentation", () => {
  const result = describeCandidateStatus("Analyzing");
  assert.equal(result.tone, "pending");
  assert.equal(result.isTerminal, false);
});

test("describeCandidateStatus maps AnalysisFailed to a failed, terminal presentation", () => {
  const result = describeCandidateStatus("AnalysisFailed");
  assert.equal(result.label, "Analysis failed");
  assert.equal(result.tone, "failed");
  assert.equal(result.isTerminal, true);
});

test("describeCandidateStatus maps BatchQueued to a pending, non-terminal presentation", () => {
  const result = describeCandidateStatus("BatchQueued");
  assert.equal(result.tone, "pending");
  assert.equal(result.isTerminal, false);
});

test("describeCandidateStatus maps BatchFailed to a failed, terminal presentation", () => {
  const result = describeCandidateStatus("BatchFailed");
  assert.equal(result.tone, "failed");
  assert.equal(result.isTerminal, true);
});

test("describeCandidateStatus maps BatchStarted to a success, terminal presentation", () => {
  const result = describeCandidateStatus("BatchStarted");
  assert.equal(result.label, "Batch started");
  assert.equal(result.tone, "success");
  assert.equal(result.isTerminal, true);
});

test("describeCandidateStatus falls back to a neutral presentation for an unknown status", () => {
  const result = describeCandidateStatus("SomethingNew");
  assert.equal(result.label, "SomethingNew");
  assert.equal(result.tone, "pending");
  assert.equal(result.isTerminal, false);
});

test("CANDIDATE_STATUS_CHECK_BACK_MESSAGE is non-empty operator-facing copy", () => {
  assert.ok(CANDIDATE_STATUS_CHECK_BACK_MESSAGE.length > 0);
});
