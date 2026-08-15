import { test } from "node:test";
import assert from "node:assert/strict";

import { buildEvidenceUrl } from "./evidence-links";

// ND-13 — the detail view exposes navigable evidence: the actual channels and videos backing the
// recommendation must be reachable, not summarized away. `NicheDiscoveryEvidenceRecord` has no
// stored URL column (design §9); this pure function derives the canonical URL from the source key
// and the normalized external reference (never from a raw provider payload).

test("builds a channel URL for the youtube-data-api source", () => {
  assert.equal(
    buildEvidenceUrl("Channel", "youtube-data-api", "UC_example"),
    "https://www.youtube.com/channel/UC_example"
  );
});

test("builds a video URL for the youtube-data-api source", () => {
  assert.equal(
    buildEvidenceUrl("Video", "youtube-data-api", "abc123"),
    "https://www.youtube.com/watch?v=abc123"
  );
});

test("returns null for an unrecognised source key rather than fabricating a URL", () => {
  assert.equal(buildEvidenceUrl("Channel", "unknown-source", "x"), null);
});

test("returns null when the external reference is blank", () => {
  assert.equal(buildEvidenceUrl("Video", "youtube-data-api", ""), null);
});
