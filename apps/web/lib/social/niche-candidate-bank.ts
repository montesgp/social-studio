/**
 * Pure decision logic for the NICHE CANDIDATE BANK on the By Search tab.
 *
 * Discovery serves candidates from a stored bank instead of re-scraping. That is only safe if the
 * operator can see it: two things have to be true on screen at all times —
 *
 * 1. the shortlist says where its candidates came from and how old the stored ones are, and
 * 2. the only way to spend money is an explicit, clearly-labelled refresh.
 *
 * This module authors no HTTP and no React. It decides what the screen is allowed to say and what the
 * refresh affordance is allowed to do, so both are testable without rendering anything.
 */

/** The one network with a working niche-search adapter today, and therefore the sensible default. */
const TIKTOK_NETWORK_CODE = "tiktok";

/**
 * Which network the By Search selector starts on. TikTok wins whenever the catalog offers it — it is
 * the only network whose niche search can actually run — and the comparison is case-insensitive
 * because the catalog owns the casing of the code it returns, not this module.
 *
 * Returns the catalog's OWN spelling of the code, never a normalised one: the value goes straight back
 * into a `<select>` whose options come from that same catalog.
 */
export function resolveDefaultSearchNetwork(availableNetworkCodes: readonly string[]): string {
  const tiktok = availableNetworkCodes.find(
    (code) => code.trim().toLowerCase() === TIKTOK_NETWORK_CODE
  );

  return tiktok ?? availableNetworkCodes[0] ?? "";
}

// ── where the candidates came from ───────────────────────────────────────────

/**
 * `bank` — served from the stored reserve, nothing was scraped and nothing was billed.
 * `fresh` — this run scraped, and it cost money.
 */
export type CandidateSourceOrigin = "bank" | "fresh";

/** The fields of `ContentWorkflowStatus` this decision actually depends on. */
export interface CandidateSourceStatusLike {
  resultOrigin?: string | null;
  bankObservedAtUtc?: string | null;
}

export interface CandidateSourceSummary {
  origin: CandidateSourceOrigin;
  /** Short badge text. */
  label: string;
  /** One sentence the operator can act on. */
  detail: string;
  /** Whole days since the oldest stored candidate was observed; `null` for a fresh run. */
  ageInDays: number | null;
}

const MS_PER_DAY = 24 * 60 * 60 * 1000;

function parseTimestamp(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }

  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

/**
 * How old the stored candidates are, in plain words. Returns `null` when there is no usable timestamp
 * — an age we cannot compute is left unsaid rather than rendered as "0 days old", which would read as
 * a freshness claim the data does not support.
 */
export function describeStoredAge(
  observedAtUtc: string | null | undefined,
  nowIso: string
): string | null {
  const observedAt = parseTimestamp(observedAtUtc);
  const now = parseTimestamp(nowIso);

  if (observedAt === null || now === null) {
    return null;
  }

  const days = Math.floor((now - observedAt) / MS_PER_DAY);

  if (days <= 0) {
    return "stored today";
  }

  return days === 1 ? "1 day old" : `${days} days old`;
}

/**
 * What the shortlist says about the provenance of what it is showing. Returns `null` when the backend
 * reported no origin at all (a session still running, or an older API): an unknown provenance is left
 * blank, never guessed, because guessing "fresh" is exactly the silent-staleness failure this exists
 * to prevent.
 */
export function summariseCandidateSource(
  status: CandidateSourceStatusLike,
  nowIso: string
): CandidateSourceSummary | null {
  const origin = status.resultOrigin?.trim().toLowerCase();

  if (origin !== "bank" && origin !== "fresh") {
    return null;
  }

  if (origin === "fresh") {
    return {
      origin: "fresh",
      label: "Fresh scrape",
      detail: "Fresh candidates, scraped from the network in this run — it was billed.",
      ageInDays: null,
    };
  }

  const observedAt = parseTimestamp(status.bankObservedAtUtc);
  const now = parseTimestamp(nowIso);
  const ageInDays =
    observedAt === null || now === null ? null : Math.max(0, Math.floor((now - observedAt) / MS_PER_DAY));

  const age = describeStoredAge(status.bankObservedAtUtc, nowIso);

  return {
    origin: "bank",
    label: "From the stored bank",
    detail: age
      ? `These candidates came from the stored bank (${age}) — nothing was scraped and nothing was billed. Use Refresh to scrape again.`
      : "These candidates came from the stored bank — nothing was scraped and nothing was billed. Use Refresh to scrape again.",
    ageInDays,
  };
}

// ── the explicit refresh ─────────────────────────────────────────────────────

/**
 * The consequence, stated plainly. The whole model rests on the operator opting in with their eyes
 * open, so this copy names the billed run rather than softening it into "get newer results".
 */
export const REFRESH_BILLING_WARNING =
  "Refresh starts a new, billed Apify run for this client's niche instead of serving the candidates already stored. Only use it when the stored ones are too old or too few.";

export interface RefreshGuardInput {
  /** The normalized discovery criterion currently selected. */
  criterion: string;
  /** The selected client, or an empty string when none is selected. */
  clientId: string;
  /** Whether a discovery is already running. */
  isDiscovering: boolean;
}

/**
 * Whether the operator may start a billed refresh right now. Niche is the only criterion with a paid
 * niche-search run behind it — reference and own accounts reuse the already-configured profile
 * scraping and have no bank to refresh — so offering it elsewhere would promise something that does
 * not happen.
 */
export function canStartRefresh(input: RefreshGuardInput): boolean {
  return (
    input.criterion === "niche" && input.clientId.trim().length > 0 && !input.isDiscovering
  );
}

/**
 * Why the refresh is unavailable, or `null` when it is available. A disabled control with no reason
 * is the same class of silence this change removes.
 */
export function getRefreshBlockedReason(input: RefreshGuardInput): string | null {
  if (canStartRefresh(input)) {
    return null;
  }

  if (input.isDiscovering) {
    return "A discovery is already running.";
  }

  if (input.clientId.trim().length === 0) {
    return "Select a client first.";
  }

  return "Only the niche criterion has a billed search to refresh — reference and own accounts reuse the profile scraping you already configured.";
}
