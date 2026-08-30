import assert from "node:assert/strict";
import { test } from "node:test";
import { formatEta, summarizeEnqueueResponse, type EnqueueWorkflowCandidatesResponse } from "./workflow-enqueue-summary";

function response(overrides: Partial<EnqueueWorkflowCandidatesResponse> = {}): EnqueueWorkflowCandidatesResponse {
  return {
    workflowId: "wf-1",
    candidates: [],
    estimatedMinutes: 12,
    jobsUrl: "/workflow/progress/wf-1",
    ...overrides
  };
}

test("summarizeEnqueueResponse counts accepted and rejected candidates", () => {
  const summary = summarizeEnqueueResponse(
    response({
      candidates: [
        { candidateId: "c1", accepted: true, rejectionReason: null },
        { candidateId: "c2", accepted: false, rejectionReason: "empty composition" },
        { candidateId: "c3", accepted: true, rejectionReason: null }
      ]
    })
  );

  assert.equal(summary.acceptedCount, 2);
  assert.equal(summary.rejectedCount, 1);
  assert.equal(summary.totalCount, 3);
});

test("summarizeEnqueueResponse never blocks on a poll — it is a pure one-shot formatter of the enqueue response", () => {
  const summary = summarizeEnqueueResponse(response({ estimatedMinutes: 8 }));
  assert.equal(summary.etaMessage, "Estimated time: about 8 minutes");
  assert.match(summary.jobsLinkLabel, /progress/i);
});

test("formatEta: zero or negative minutes", () => {
  assert.equal(formatEta(0), "Estimated time: a few minutes");
  assert.equal(formatEta(-5), "Estimated time: a few minutes");
});

test("formatEta: singular minute", () => {
  assert.equal(formatEta(1), "Estimated time: about 1 minute");
});

test("formatEta: plural minutes under an hour", () => {
  assert.equal(formatEta(45), "Estimated time: about 45 minutes");
});

test("formatEta: whole hours", () => {
  assert.equal(formatEta(60), "Estimated time: about 1 hour");
  assert.equal(formatEta(120), "Estimated time: about 2 hours");
});

test("formatEta: hours with remaining minutes", () => {
  assert.equal(formatEta(90), "Estimated time: about 1 hour 30 min");
});
