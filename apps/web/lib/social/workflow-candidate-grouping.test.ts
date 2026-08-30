import assert from "node:assert/strict";
import { test } from "node:test";
import { isProcessedCandidateStatus, splitCandidatesByProcessedState } from "./workflow-candidate-grouping";

interface FakeCandidate {
  id: string;
  status: string;
}

function candidate(id: string, status: string): FakeCandidate {
  return { id, status };
}

test("isProcessedCandidateStatus is true only for BatchStarted", () => {
  assert.equal(isProcessedCandidateStatus("BatchStarted"), true);
  assert.equal(isProcessedCandidateStatus("Discovered"), false);
  assert.equal(isProcessedCandidateStatus("Queued"), false);
  assert.equal(isProcessedCandidateStatus("Recovered"), false);
  assert.equal(isProcessedCandidateStatus("AnalysisFailed"), false);
});

test("splitCandidatesByProcessedState separates BatchStarted candidates into the processed bucket", () => {
  const results = [
    candidate("a", "Discovered"),
    candidate("b", "BatchStarted"),
    candidate("c", "Queued"),
    candidate("d", "BatchStarted"),
    candidate("e", "Recovered")
  ];

  const grouping = splitCandidatesByProcessedState(results);

  assert.deepEqual(
    grouping.active.map((r) => r.id),
    ["a", "c", "e"]
  );
  assert.deepEqual(
    grouping.processed.map((r) => r.id),
    ["b", "d"]
  );
});

test("splitCandidatesByProcessedState returns empty buckets for an empty list", () => {
  const grouping = splitCandidatesByProcessedState([]);
  assert.deepEqual(grouping, { active: [], processed: [] });
});

test("Recovered candidates land back in the active bucket even though generation already happened once", () => {
  const grouping = splitCandidatesByProcessedState([candidate("a", "Recovered")]);
  assert.deepEqual(grouping.active.map((r) => r.id), ["a"]);
  assert.deepEqual(grouping.processed, []);
});
