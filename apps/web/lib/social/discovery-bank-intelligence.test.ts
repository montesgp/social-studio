import { test } from "node:test";
import assert from "node:assert/strict";

import {
  buildChatHistory,
  describeExecutedQuery,
  formatMetricValue,
  isAskable,
  isBankRequestReady,
  summariseCitations,
  toChatMessage,
  BANK_EMPTY_HEADLINE,
  type DiscoveryBankAnswer,
} from "./discovery-bank-intelligence";

const supportedQueries = [
  {
    key: "top_by_metric",
    label: "Top candidates by a metric",
    description: "Ranks the matching candidates by one stored metric.",
    exampleQuestion: "Top 5 by views",
  },
];

function answer(overrides: Partial<DiscoveryBankAnswer> = {}): DiscoveryBankAnswer {
  return {
    question: "top 5 by views",
    status: "Answered",
    answer: "The strongest candidate is @alpha.",
    executedQuery: {
      queryKey: "top_by_metric",
      label: "Top candidates by a metric",
      metric: "views",
      aggregate: null,
      limit: 5,
      networkKey: null,
      hashtag: null,
      authorHandle: null,
      publishedFromUtc: null,
      publishedToUtc: null,
      interpretation: null,
    },
    citations: [],
    groups: [],
    matchedCount: 0,
    aggregateValue: null,
    supportedQueries,
    ...overrides,
  };
}

// ── what the operator may send ───────────────────────────────────────────────

test("isAskable refuses a blank question", () => {
  assert.equal(isAskable("   ", false), false);
});

test("isAskable refuses a second question while one is in flight", () => {
  assert.equal(isAskable("top 5 by views", true), false);
});

test("isAskable accepts a real question when nothing is running", () => {
  assert.equal(isAskable("top 5 by views", false), true);
});

test("isBankRequestReady refuses a request before the session token has resolved", () => {
  assert.equal(isBankRequestReady(""), false);
  assert.equal(isBankRequestReady("   "), false);
  assert.equal(isBankRequestReady(null), false);
  assert.equal(isBankRequestReady(undefined), false);
});

test("isBankRequestReady accepts a resolved session token", () => {
  assert.equal(isBankRequestReady("eyJhbGciOi.header.signature"), true);
});

// ── how an answer becomes a chat message ─────────────────────────────────────

test("an answered question keeps the narration and carries its citations", () => {
  const message = toChatMessage(
    answer({
      status: "Answered",
      matchedCount: 412,
      citations: [
        {
          candidateKey: "a",
          networkKey: "tiktok",
          contentUrl: "https://example.test/a",
          authorHandle: "@alpha",
          title: "A",
          hashtags: ["fitness"],
          publishedAtUtc: null,
          observedAtUtc: "2026-08-01T00:00:00Z",
          views: 900,
          likes: null,
          comments: null,
          shares: null,
          saves: null,
          opportunityScore: null,
          metricValue: 900,
        },
      ],
    })
  );

  assert.equal(message.role, "assistant");
  assert.equal(message.text, "The strongest candidate is @alpha.");
  assert.equal(message.citations.length, 1);
  assert.equal(message.citations[0].contentUrl, "https://example.test/a");
  assert.ok(message.queryLabel?.includes("Top candidates by a metric"));
});

test("an unsupported question is shown as such, with no query label and no citations", () => {
  const message = toChatMessage(
    answer({
      status: "Unsupported",
      answer: "That question does not map to any analysis this tool can run.",
      executedQuery: null,
      citations: [],
    })
  );

  assert.equal(message.tone, "refused");
  assert.equal(message.queryLabel, null);
  assert.equal(message.citations.length, 0);
  assert.equal(message.supportedQueries.length, 1);
});

test("an empty bank is shown as guidance, not as a failed answer", () => {
  const message = toChatMessage(answer({ status: "BankEmpty", answer: BANK_EMPTY_HEADLINE }));

  assert.equal(message.tone, "empty");
  assert.equal(message.queryLabel, null);
});

// ── the query, restated for the operator ─────────────────────────────────────

test("describeExecutedQuery names the query, the metric and the size of the set it ran over", () => {
  const described = describeExecutedQuery(answer({ matchedCount: 412 }));

  assert.ok(described.includes("Top candidates by a metric"));
  assert.ok(described.includes("views"));
  assert.ok(described.includes("412"));
});

test("describeExecutedQuery lists every filter that narrowed the set", () => {
  const described = describeExecutedQuery(
    answer({
      matchedCount: 7,
      executedQuery: {
        queryKey: "top_by_metric",
        label: "Top candidates by a metric",
        metric: "comments",
        aggregate: null,
        limit: 5,
        networkKey: "tiktok",
        hashtag: "fitness",
        authorHandle: "@alpha",
        publishedFromUtc: "2026-01-01T00:00:00Z",
        publishedToUtc: null,
        interpretation: null,
      },
    })
  );

  assert.ok(described.includes("tiktok"));
  assert.ok(described.includes("fitness"));
  assert.ok(described.includes("@alpha"));
});

test("describeExecutedQuery says nothing when no query ran", () => {
  assert.equal(describeExecutedQuery(answer({ executedQuery: null })), "");
});

// ── citations ────────────────────────────────────────────────────────────────

test("summariseCitations reports how many of how many were cited", () => {
  const summary = summariseCitations(3, 412);
  assert.ok(summary.includes("3"));
  assert.ok(summary.includes("412"));
});

test("summariseCitations does not claim a subset when everything matched was cited", () => {
  assert.equal(summariseCitations(4, 4).includes("of 4"), false);
});

test("summariseCitations is empty when nothing was cited", () => {
  assert.equal(summariseCitations(0, 412), "");
});

// ── numbers ──────────────────────────────────────────────────────────────────

test("formatMetricValue renders whole counts without a decimal tail", () => {
  assert.equal(formatMetricValue(1200), "1,200");
});

test("formatMetricValue keeps precision for fractional values", () => {
  assert.equal(formatMetricValue(0.755), "0.76");
});

test("formatMetricValue reports a missing value as an em dash, never as zero", () => {
  assert.equal(formatMetricValue(null), "—");
  assert.equal(formatMetricValue(undefined), "—");
});

// ── history sent back to the backend ─────────────────────────────────────────

test("buildChatHistory forwards the turns in order, dropping empty ones", () => {
  const history = buildChatHistory([
    { id: "1", role: "user", text: "top 5 by views", tone: "normal", citations: [], groups: [], queryLabel: null, supportedQueries: [], matchedCount: 0 },
    { id: "2", role: "assistant", text: "  ", tone: "normal", citations: [], groups: [], queryLabel: null, supportedQueries: [], matchedCount: 0 },
    { id: "3", role: "assistant", text: "@alpha leads.", tone: "normal", citations: [], groups: [], queryLabel: null, supportedQueries: [], matchedCount: 0 },
  ]);

  assert.deepEqual(history, [
    { role: "user", text: "top 5 by views" },
    { role: "assistant", text: "@alpha leads." },
  ]);
});
