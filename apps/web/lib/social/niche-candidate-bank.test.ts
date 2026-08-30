import { test } from "node:test";
import assert from "node:assert/strict";
import {
  REFRESH_BILLING_WARNING,
  canStartRefresh,
  describeStoredAge,
  getRefreshBlockedReason,
  resolveDefaultSearchNetwork,
  summariseCandidateSource,
} from "./niche-candidate-bank.ts";

// ── TikTok default ───────────────────────────────────────────────────────────

test("resolveDefaultSearchNetwork: TikTok wins when it is available", () => {
  assert.equal(resolveDefaultSearchNetwork(["instagram", "tiktok", "youtube"]), "tiktok");
});

test("resolveDefaultSearchNetwork: TikTok wins whatever case the catalog reports it in", () => {
  assert.equal(resolveDefaultSearchNetwork(["Instagram", "TikTok"]), "TikTok");
});

test("resolveDefaultSearchNetwork: falls back to the first network when TikTok is absent", () => {
  assert.equal(resolveDefaultSearchNetwork(["instagram", "youtube"]), "instagram");
});

test("resolveDefaultSearchNetwork: no networks at all resolves to an empty selection", () => {
  assert.equal(resolveDefaultSearchNetwork([]), "");
});

// ── where the candidates came from ───────────────────────────────────────────

test("summariseCandidateSource: stored candidates are reported as stored, with their age", () => {
  const summary = summariseCandidateSource(
    { resultOrigin: "bank", bankObservedAtUtc: "2026-08-05T10:00:00Z" },
    "2026-08-12T10:00:00Z"
  );

  assert.equal(summary?.origin, "bank");
  assert.equal(summary?.ageInDays, 7);
  assert.match(summary!.detail, /stored/i);
  assert.match(summary!.detail, /7 days old/);
});

test("summariseCandidateSource: a fresh run says so and carries no age", () => {
  const summary = summariseCandidateSource(
    { resultOrigin: "fresh", bankObservedAtUtc: null },
    "2026-08-12T10:00:00Z"
  );

  assert.equal(summary?.origin, "fresh");
  assert.equal(summary?.ageInDays, null);
  assert.match(summary!.detail, /fresh/i);
});

test("summariseCandidateSource: an origin the backend did not report is never guessed", () => {
  assert.equal(summariseCandidateSource({}, "2026-08-12T10:00:00Z"), null);
  assert.equal(summariseCandidateSource({ resultOrigin: null }, "2026-08-12T10:00:00Z"), null);
  assert.equal(summariseCandidateSource({ resultOrigin: "" }, "2026-08-12T10:00:00Z"), null);
});

test("summariseCandidateSource: stored candidates with no recorded age still say they are stored", () => {
  const summary = summariseCandidateSource({ resultOrigin: "bank" }, "2026-08-12T10:00:00Z");

  assert.equal(summary?.origin, "bank");
  assert.equal(summary?.ageInDays, null);
  assert.match(summary!.detail, /stored/i);
});

test("describeStoredAge: same day, one day and many days each read naturally", () => {
  assert.equal(describeStoredAge("2026-08-12T09:00:00Z", "2026-08-12T10:00:00Z"), "stored today");
  assert.equal(describeStoredAge("2026-08-11T09:00:00Z", "2026-08-12T10:00:00Z"), "1 day old");
  assert.equal(describeStoredAge("2026-07-13T10:00:00Z", "2026-08-12T10:00:00Z"), "30 days old");
});

test("describeStoredAge: an unparseable timestamp is never rendered as an age", () => {
  assert.equal(describeStoredAge("not-a-date", "2026-08-12T10:00:00Z"), null);
  assert.equal(describeStoredAge(null, "2026-08-12T10:00:00Z"), null);
});

// ── the explicit refresh ─────────────────────────────────────────────────────

test("REFRESH_BILLING_WARNING: names the billed run explicitly", () => {
  assert.match(REFRESH_BILLING_WARNING, /billed/i);
  assert.match(REFRESH_BILLING_WARNING, /Apify/);
});

test("canStartRefresh: allowed for a niche search with a client selected and nothing running", () => {
  assert.equal(canStartRefresh({ criterion: "niche", clientId: "c1", isDiscovering: false }), true);
});

test("canStartRefresh: never while a discovery is already running", () => {
  assert.equal(canStartRefresh({ criterion: "niche", clientId: "c1", isDiscovering: true }), false);
});

test("canStartRefresh: never without a client", () => {
  assert.equal(canStartRefresh({ criterion: "niche", clientId: "", isDiscovering: false }), false);
});

test("canStartRefresh: only the niche criterion has a paid run to refresh", () => {
  assert.equal(canStartRefresh({ criterion: "references", clientId: "c1", isDiscovering: false }), false);
  assert.equal(canStartRefresh({ criterion: "own", clientId: "c1", isDiscovering: false }), false);
});

test("getRefreshBlockedReason: explains the non-niche case instead of silently disabling", () => {
  const reason = getRefreshBlockedReason({ criterion: "references", clientId: "c1", isDiscovering: false });

  assert.ok(reason);
  assert.match(reason!, /niche/i);
});

test("getRefreshBlockedReason: no reason when the refresh is allowed", () => {
  assert.equal(getRefreshBlockedReason({ criterion: "niche", clientId: "c1", isDiscovering: false }), null);
});
