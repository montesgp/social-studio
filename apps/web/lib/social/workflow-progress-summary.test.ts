import assert from "node:assert/strict";
import { test } from "node:test";
import { deriveProgressBucket, summarizeWorkflowProgress } from "./workflow-progress-summary";
import type { ContentWorkflowCandidateStatus } from "@/lib/api/types";

function candidate(overrides: Partial<ContentWorkflowCandidateStatus> = {}): ContentWorkflowCandidateStatus {
  return {
    candidateId: "c1",
    title: "Candidate",
    status: "Queued",
    failureCode: null,
    ...overrides
  };
}

test("deriveProgressBucket maps Queued to pending", () => {
  assert.equal(deriveProgressBucket("Queued"), "pending");
});

test("deriveProgressBucket maps Analyzing and BatchQueued to working", () => {
  assert.equal(deriveProgressBucket("Analyzing"), "working");
  assert.equal(deriveProgressBucket("BatchQueued"), "working");
});

test("deriveProgressBucket maps BatchStarted to completed", () => {
  assert.equal(deriveProgressBucket("BatchStarted"), "completed");
});

test("deriveProgressBucket maps AnalysisFailed and BatchFailed to failed", () => {
  assert.equal(deriveProgressBucket("AnalysisFailed"), "failed");
  assert.equal(deriveProgressBucket("BatchFailed"), "failed");
});

test("deriveProgressBucket falls back to pending for an unknown status", () => {
  assert.equal(deriveProgressBucket("SomethingNew"), "pending");
});

test("summarizeWorkflowProgress counts candidates per bucket", () => {
  const summary = summarizeWorkflowProgress([
    candidate({ candidateId: "a", status: "Queued" }),
    candidate({ candidateId: "b", status: "Analyzing" }),
    candidate({ candidateId: "c", status: "BatchQueued" }),
    candidate({ candidateId: "d", status: "BatchStarted" }),
    candidate({ candidateId: "e", status: "BatchFailed", failureCode: "some_error" }),
    candidate({ candidateId: "f", status: "AnalysisFailed", failureCode: "timeout" })
  ]);

  assert.deepEqual(summary, {
    pending: 1,
    working: 2,
    completed: 1,
    failed: 2,
    total: 6
  });
});

test("summarizeWorkflowProgress returns all-zero counts for an empty candidate list", () => {
  const summary = summarizeWorkflowProgress([]);
  assert.deepEqual(summary, { pending: 0, working: 0, completed: 0, failed: 0, total: 0 });
});
