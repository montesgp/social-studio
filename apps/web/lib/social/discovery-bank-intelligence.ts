/**
 * Pure decision logic for the AI ANALYSIS tool — the chat that interrogates the discovery candidate bank.
 *
 * The tool's whole claim is that its numbers are computed by the SYSTEM, not by the model. That claim is
 * only worth anything if the screen makes it checkable, so this module owns three things and no React:
 *
 * 1. what the operator is allowed to send;
 * 2. how an answer is restated — WHICH query ran, over HOW MANY candidates, under WHICH filters;
 * 3. how the cited candidates are rendered, so every claim can be opened and verified.
 *
 * Keeping it here rather than in the `.tsx` layer is what makes it testable: there is no React component
 * test harness in this app, so anything a component decides silently is untested by construction.
 */

// ── the wire shape ───────────────────────────────────────────────────────────

export type DiscoveryBankAnswerStatus = "Answered" | "Unsupported" | "BankEmpty";

export interface DiscoveryBankSupportedQuery {
  key: string;
  label: string;
  description: string;
  exampleQuestion: string;
}

export interface DiscoveryBankExecutedQuery {
  queryKey: string;
  label: string;
  metric: string;
  aggregate: string | null;
  limit: number;
  networkKey: string | null;
  hashtag: string | null;
  authorHandle: string | null;
  publishedFromUtc: string | null;
  publishedToUtc: string | null;
  interpretation: string | null;
}

export interface DiscoveryBankCitation {
  candidateKey: string;
  networkKey: string;
  contentUrl: string;
  authorHandle: string;
  title: string;
  hashtags: string[];
  publishedAtUtc: string | null;
  observedAtUtc: string;
  views: number | null;
  likes: number | null;
  comments: number | null;
  shares: number | null;
  saves: number | null;
  opportunityScore: number | null;
  metricValue: number | null;
}

export interface DiscoveryBankGroup {
  key: string;
  candidateCount: number;
  metricTotal: number;
}

export interface DiscoveryBankAnswer {
  question: string;
  status: DiscoveryBankAnswerStatus;
  answer: string;
  executedQuery: DiscoveryBankExecutedQuery | null;
  citations: DiscoveryBankCitation[];
  groups: DiscoveryBankGroup[];
  matchedCount: number;
  aggregateValue: number | null;
  supportedQueries: DiscoveryBankSupportedQuery[];
}

// ── the chat as the screen holds it ──────────────────────────────────────────

/** `refused` and `empty` are not errors — they are the tool declining to improvise. */
export type ChatTone = "normal" | "refused" | "empty";

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  text: string;
  tone: ChatTone;
  /** Restates the query that produced this answer; `null` when none ran. */
  queryLabel: string | null;
  citations: DiscoveryBankCitation[];
  groups: DiscoveryBankGroup[];
  matchedCount: number;
  supportedQueries: DiscoveryBankSupportedQuery[];
}

export const BANK_EMPTY_HEADLINE =
  "There is no discovery data to analyse yet. Run a niche search first — Single Analyzer → By Search.";

/**
 * The one-line promise the page makes, stated where the operator reads the answers. It is not decoration:
 * a chat panel that looks like every other LLM chat will be READ like one, and the rankings here are
 * exactly the kind an LLM would have gotten wrong.
 */
export const COMPUTED_NOT_GUESSED_NOTE =
  "Rankings, counts and totals are computed by the system over the whole bank. The assistant chooses which query to run and describes the result — it never does the arithmetic.";

// ── what may be sent ─────────────────────────────────────────────────────────

/** A blank question buys nothing, and a second question in flight would race the first. */
export function isAskable(question: string, isAsking: boolean): boolean {
  return question.trim().length > 0 && !isAsking;
}

/**
 * Both bank endpoints require authorization, and the session token is read from browser storage AFTER
 * the first client render — so it is empty for one tick on every mount. Firing then produces a 401 that
 * the page can only report as "the bank could not be loaded", which is a lie: the bank was never asked.
 * Waiting for a resolved token is the difference between a real failure and a race with our own mount.
 */
export function isBankRequestReady(token: string | null | undefined): boolean {
  return typeof token === "string" && token.trim().length > 0;
}

// ── an answer, restated ──────────────────────────────────────────────────────

/**
 * Names the query that ran, the metric it ranked on, every filter that narrowed the bank, and how many
 * candidates matched. This is the operator's audit trail: without it, "top 5 by views" and "top 5 by
 * views among TikTok posts since January" look identical on screen while meaning very different things.
 *
 * Returns an empty string when no query ran — there is nothing honest to say about a query that did not
 * happen.
 */
export function describeExecutedQuery(answer: DiscoveryBankAnswer): string {
  const query = answer.executedQuery;
  if (!query) {
    return "";
  }

  const parts: string[] = [`${query.label} — ${query.metric}`];

  if (query.aggregate) {
    parts.push(query.aggregate);
  }

  const filters: string[] = [];
  if (query.networkKey) filters.push(`network ${query.networkKey}`);
  if (query.hashtag) filters.push(`hashtag ${query.hashtag}`);
  if (query.authorHandle) filters.push(`author ${query.authorHandle}`);
  if (query.publishedFromUtc) filters.push(`published from ${query.publishedFromUtc}`);
  if (query.publishedToUtc) filters.push(`published to ${query.publishedToUtc}`);

  if (filters.length > 0) {
    parts.push(filters.join(", "));
  }

  parts.push(`${answer.matchedCount} candidate${answer.matchedCount === 1 ? "" : "s"} matched`);

  return parts.join(" · ");
}

/**
 * "3 of 412" — the sentence that stops a capped shortlist reading as the whole set. When every matched
 * candidate is on screen there is no subset to disclose, so the "of N" is dropped rather than repeated
 * as noise.
 */
export function summariseCitations(citedCount: number, matchedCount: number): string {
  if (citedCount <= 0) {
    return "";
  }

  const noun = citedCount === 1 ? "candidate" : "candidates";

  return citedCount >= matchedCount
    ? `${citedCount} ${noun} cited`
    : `${citedCount} of ${matchedCount} matching ${noun} cited`;
}

export function toChatMessage(answer: DiscoveryBankAnswer, id: string = ""): ChatMessage {
  const tone: ChatTone =
    answer.status === "Unsupported" ? "refused" : answer.status === "BankEmpty" ? "empty" : "normal";

  return {
    id,
    role: "assistant",
    text: answer.answer,
    tone,
    queryLabel: answer.status === "Answered" ? describeExecutedQuery(answer) : null,
    citations: answer.status === "Answered" ? answer.citations : [],
    groups: answer.status === "Answered" ? answer.groups : [],
    matchedCount: answer.matchedCount,
    supportedQueries: answer.supportedQueries,
  };
}

// ── numbers ──────────────────────────────────────────────────────────────────

/**
 * Counts read as counts and scores keep their precision. A missing value renders as an em dash and never
 * as `0` — the bank genuinely does not know some of these numbers, and printing zero would state a fact
 * the data does not support.
 */
export function formatMetricValue(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "—";
  }

  if (Number.isInteger(value)) {
    return value.toLocaleString("en-US");
  }

  return value.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

// ── history ──────────────────────────────────────────────────────────────────

/**
 * The chat so far, in the shape the backend planner accepts. Empty turns are dropped rather than sent:
 * they cost prompt budget and tell the planner nothing.
 *
 * No trimming happens here on purpose — the backend owns the cap, so a change to it takes effect without
 * a frontend deploy and the two can never disagree about how much history was actually used.
 */
export function buildChatHistory(messages: ChatMessage[]): { role: "user" | "assistant"; text: string }[] {
  return messages
    .filter((message) => message.text.trim().length > 0)
    .map((message) => ({ role: message.role, text: message.text.trim() }));
}
