import { test } from "node:test";
import assert from "node:assert/strict";
import {
  buildAnalyzeSelectedCandidatePayload,
  discoveryOutcomeOffersSettingsLink,
  discoveryStepIndex,
  getDiscoveryOutcomeMessage,
  getNicheSearchBlockedMessage,
  getNicheSearchReadiness,
  isDiscoveryTerminal,
  isNicheSearchBlocked,
  mapDiscoveryResultsToShortlistCandidates,
  resolveDiscoveryOutcome,
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

test("getNicheSearchReadiness: ready when the network's checklist item is completed", () => {
  const checklistGroups = [
    {
      key: "single-analyzer",
      items: [
        { key: "single-analyzer-niche-search-actor-tiktok", isCompleted: true },
        { key: "some-other-item", isCompleted: false },
      ],
    },
  ];

  assert.equal(getNicheSearchReadiness(checklistGroups, "tiktok"), "ready");
});

test("getNicheSearchReadiness: unconfigured when the network has an item that is not completed", () => {
  assert.equal(
    getNicheSearchReadiness(
      [{ key: "single-analyzer", items: [{ key: "single-analyzer-niche-search-actor-tiktok", isCompleted: false }] }],
      "tiktok"
    ),
    "unconfigured"
  );
});

test("getNicheSearchReadiness: unsupported when the network has no item at all", () => {
  // Instagram/YouTube have no adapter, so the backend emits no item for them — absence means
  // "not supported yet", never "go configure an actor".
  const checklistGroups = [
    { key: "single-analyzer", items: [{ key: "single-analyzer-niche-search-actor-tiktok", isCompleted: true }] },
  ];

  assert.equal(getNicheSearchReadiness(checklistGroups, "instagram"), "unsupported");
  assert.equal(getNicheSearchReadiness([], "tiktok"), "unsupported");
  assert.equal(getNicheSearchReadiness([{ key: "generator", items: [] }], "tiktok"), "unsupported");
});

test("getNicheSearchReadiness: no selected network takes the best outcome across supported networks", () => {
  const checklistGroups = [
    {
      key: "single-analyzer",
      items: [
        { key: "single-analyzer-niche-search-actor-tiktok", isCompleted: true },
        { key: "single-analyzer-niche-search-actor-linkedin", isCompleted: false },
      ],
    },
  ];

  assert.equal(getNicheSearchReadiness(checklistGroups, ""), "ready");
  assert.equal(
    getNicheSearchReadiness(
      [{ key: "single-analyzer", items: [{ key: "single-analyzer-niche-search-actor-tiktok", isCompleted: false }] }],
      ""
    ),
    "unconfigured"
  );
});

test("isNicheSearchBlocked: only the niche criterion is gated on the actor checklist item", () => {
  assert.equal(isNicheSearchBlocked("niche", "unconfigured"), true);
  assert.equal(isNicheSearchBlocked("niche", "unsupported"), true);
  assert.equal(isNicheSearchBlocked("niche", "ready"), false);
  assert.equal(isNicheSearchBlocked("references", "unsupported"), false);
  assert.equal(isNicheSearchBlocked("own", "unsupported"), false);
});

test("getNicheSearchBlockedMessage: an unsupported network is told it is unsupported, not misconfigured", () => {
  const message = getNicheSearchBlockedMessage("niche", "unsupported", "Instagram");

  assert.ok(message);
  assert.match(message, /isn't supported yet/);
  assert.doesNotMatch(message, /TikTok niche-search actor/);
  assert.match(message, /Instagram/);
});

test("getNicheSearchBlockedMessage: a supported but unrouted network is told to configure an actor", () => {
  const message = getNicheSearchBlockedMessage("niche", "unconfigured", "TikTok");

  assert.ok(message);
  assert.match(message, /isn't configured yet/);
  assert.match(message, /TikTok/);
});

test("getNicheSearchBlockedMessage: null when the stage can run or the criterion is not niche", () => {
  assert.equal(getNicheSearchBlockedMessage("niche", "ready", "TikTok"), null);
  assert.equal(getNicheSearchBlockedMessage("references", "unsupported", "Instagram"), null);
});

// ── discovery run outcome (start + poll) ──────────────────────────────────────

test("resolveDiscoveryOutcome: an in-progress session is still running", () => {
  assert.equal(resolveDiscoveryOutcome({ status: "in_progress", totalResults: 0 }), "running");
});

test("resolveDiscoveryOutcome: an unknown status is treated as still running, never as a terminal failure", () => {
  assert.equal(resolveDiscoveryOutcome({ status: "queued", totalResults: 0 }), "running");
});

test("resolveDiscoveryOutcome: completed with candidates is distinct from completed empty", () => {
  assert.equal(resolveDiscoveryOutcome({ status: "completed", totalResults: 7 }), "candidates");
  assert.equal(resolveDiscoveryOutcome({ status: "completed", totalResults: 0 }), "empty");
});

test("resolveDiscoveryOutcome: a provider failure is failed, NOT empty", () => {
  const outcome = resolveDiscoveryOutcome({ status: "failed", totalResults: 0 });

  assert.equal(outcome, "failed");
  assert.notEqual(outcome, "empty");
});

test("resolveDiscoveryOutcome: an unconfigured stage stays unconfigured", () => {
  assert.equal(resolveDiscoveryOutcome({ status: "unconfigured", totalResults: 0 }), "unconfigured");
});

test("isDiscoveryTerminal: only the four terminal backend statuses stop the poll loop", () => {
  assert.equal(isDiscoveryTerminal("completed"), true);
  assert.equal(isDiscoveryTerminal("failed"), true);
  assert.equal(isDiscoveryTerminal("unconfigured"), true);
  assert.equal(isDiscoveryTerminal("in_progress"), false);
  assert.equal(isDiscoveryTerminal("anything-else"), false);
});

test("getDiscoveryOutcomeMessage: a failed run reads as a provider failure, never as 'no candidates'", () => {
  const message = getDiscoveryOutcomeMessage("failed");

  assert.ok(message);
  assert.match(message, /could not be completed/i);
  assert.doesNotMatch(message, /no candidates/i);
});

test("getDiscoveryOutcomeMessage: an unconfigured run points the operator at settings", () => {
  const message = getDiscoveryOutcomeMessage("unconfigured");

  assert.ok(message);
  assert.match(message, /Settings \/ Integrations/);
});

test("getDiscoveryOutcomeMessage: an empty run says exactly that, with no blame", () => {
  const message = getDiscoveryOutcomeMessage("empty");

  assert.ok(message);
  assert.match(message, /no candidates/i);
  assert.doesNotMatch(message, /failed/i);
});

test("getDiscoveryOutcomeMessage: nothing to say while running or once candidates are in", () => {
  assert.equal(getDiscoveryOutcomeMessage("running"), null);
  assert.equal(getDiscoveryOutcomeMessage("candidates"), null);
});

test("discoveryOutcomeOffersSettingsLink: only the unconfigured outcome links to settings", () => {
  assert.equal(discoveryOutcomeOffersSettingsLink("unconfigured"), true);
  assert.equal(discoveryOutcomeOffersSettingsLink("failed"), false);
  assert.equal(discoveryOutcomeOffersSettingsLink("empty"), false);
  assert.equal(discoveryOutcomeOffersSettingsLink("candidates"), false);
});

test("discoveryStepIndex: maps backend step statuses onto the overlay's active index", () => {
  const steps = [
    { status: "completed" },
    { status: "in_progress" },
    { status: "pending" },
  ];

  assert.equal(discoveryStepIndex(steps, "in_progress"), 1);
  // Terminal: the overlay wants steps.length so every row renders as settled.
  assert.equal(discoveryStepIndex(steps, "completed"), 3);
  assert.equal(discoveryStepIndex(steps, "failed"), 3);
});

test("discoveryStepIndex: no reported step yet parks the overlay on the first row", () => {
  assert.equal(discoveryStepIndex([], "in_progress"), 0);
  assert.equal(discoveryStepIndex([{ status: "pending" }], "in_progress"), 0);
});
