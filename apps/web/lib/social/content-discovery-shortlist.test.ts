import { test } from "node:test";
import assert from "node:assert/strict";
import {
  buildAnalyzeSelectedCandidatePayload,
  isNicheSearchUnconfigured,
  isSingleAnalyzerNicheSearchActorConfigured,
  mapDiscoveryResultsToShortlistCandidates,
  selectSingleCandidate,
} from "./content-discovery-shortlist.ts";

test("selectSingleCandidate: selecting a candidate with nothing selected sets it", () => {
  assert.equal(selectSingleCandidate(null, "candidate-1"), "candidate-1");
});

test("selectSingleCandidate: selecting a second candidate REPLACES the first, never adds", () => {
  const afterFirst = selectSingleCandidate(null, "candidate-1");
  const afterSecond = selectSingleCandidate(afterFirst, "candidate-2");

  assert.equal(afterSecond, "candidate-2");
  assert.notEqual(afterSecond, "candidate-1");
});

test("selectSingleCandidate: selecting the already-selected candidate clears the selection", () => {
  assert.equal(selectSingleCandidate("candidate-1", "candidate-1"), null);
});

test("buildAnalyzeSelectedCandidatePayload: nothing selected returns null", () => {
  const candidates = [{ id: "candidate-1", contentUrl: "https://www.tiktok.com/@a/video/1" }];
  assert.equal(buildAnalyzeSelectedCandidatePayload(candidates, null), null);
});

test("buildAnalyzeSelectedCandidatePayload: unknown selected id returns null, never a fabricated candidate", () => {
  const candidates = [{ id: "candidate-1", contentUrl: "https://www.tiktok.com/@a/video/1" }];
  assert.equal(buildAnalyzeSelectedCandidatePayload(candidates, "candidate-does-not-exist"), null);
});

test("buildAnalyzeSelectedCandidatePayload: builds the EXISTING single-analysis start payload shape", () => {
  const candidates = [
    { id: "candidate-1", contentUrl: "https://www.tiktok.com/@a/video/1" },
    { id: "candidate-2", contentUrl: "https://www.tiktok.com/@b/video/2" },
  ];

  const payload = buildAnalyzeSelectedCandidatePayload(candidates, "candidate-2", "client-42");

  assert.deepEqual(payload, {
    url: "https://www.tiktok.com/@b/video/2",
    clientId: "client-42",
  });
});

test("buildAnalyzeSelectedCandidatePayload: omits clientId when not provided", () => {
  const candidates = [{ id: "candidate-1", contentUrl: "https://www.tiktok.com/@a/video/1" }];

  const payload = buildAnalyzeSelectedCandidatePayload(candidates, "candidate-1");

  assert.deepEqual(payload, { url: "https://www.tiktok.com/@a/video/1" });
});

test("mapDiscoveryResultsToShortlistCandidates: projects the existing workflow results endpoint shape", () => {
  const mapped = mapDiscoveryResultsToShortlistCandidates([
    {
      id: "result-1",
      title: "A great TikTok",
      networkSource: "tiktok",
      videoUrl: "https://www.tiktok.com/@a/video/1",
      thumbnailUrl: "https://cdn.example/thumb.jpg",
      views: 12345,
      likes: 678,
      comments: 9,
    },
  ]);

  assert.deepEqual(mapped, [
    {
      id: "result-1",
      contentUrl: "https://www.tiktok.com/@a/video/1",
      title: "A great TikTok",
      networkSource: "tiktok",
      thumbnailUrl: "https://cdn.example/thumb.jpg",
      views: 12345,
      likes: 678,
      comments: 9,
    },
  ]);
});

test("mapDiscoveryResultsToShortlistCandidates: empty thumbnail becomes undefined, never a broken image src", () => {
  const mapped = mapDiscoveryResultsToShortlistCandidates([
    {
      id: "result-1",
      title: "No thumbnail",
      networkSource: "instagram",
      videoUrl: "https://www.instagram.com/p/xyz/",
      thumbnailUrl: "",
      views: 0,
      likes: 0,
      comments: 0,
    },
  ]);

  assert.equal(mapped[0].thumbnailUrl, undefined);
});

test("isSingleAnalyzerNicheSearchActorConfigured: true when the checklist item is completed", () => {
  const checklistGroups = [
    {
      key: "single-analyzer",
      items: [
        { key: "single-analyzer-niche-search-actor", isCompleted: true },
        { key: "some-other-item", isCompleted: false },
      ],
    },
  ];

  assert.equal(isSingleAnalyzerNicheSearchActorConfigured(checklistGroups), true);
});

test("isSingleAnalyzerNicheSearchActorConfigured: false when the item is missing or incomplete", () => {
  assert.equal(isSingleAnalyzerNicheSearchActorConfigured([]), false);
  assert.equal(
    isSingleAnalyzerNicheSearchActorConfigured([
      { key: "single-analyzer", items: [{ key: "single-analyzer-niche-search-actor", isCompleted: false }] },
    ]),
    false
  );
  assert.equal(
    isSingleAnalyzerNicheSearchActorConfigured([{ key: "generator", items: [] }]),
    false
  );
});

test("isNicheSearchUnconfigured: only the niche criterion is gated on the actor checklist item", () => {
  assert.equal(isNicheSearchUnconfigured("niche", false), true);
  assert.equal(isNicheSearchUnconfigured("niche", true), false);
  assert.equal(isNicheSearchUnconfigured("references", false), false);
  assert.equal(isNicheSearchUnconfigured("own", false), false);
});
