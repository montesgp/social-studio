/**
 * Pure logic for the discovery shortlist single-selection action (CD-10, design D8/§8.2, WU-7).
 *
 * The operator selects EXACTLY ONE ranked candidate for instant analysis through the EXISTING
 * single-analysis path (`AnalyzeSingleContentCommand` + the existing polling overlay). This module
 * authors no HTTP calls itself — it only decides which single candidate is selected and builds the
 * exact payload the existing `/api/social/single-analyzer/analyze` endpoint already accepts. No new
 * endpoint, no new request shape.
 */

export interface DiscoveryShortlistCandidate {
  id: string;
  contentUrl: string;
}

/**
 * Shape of one item as returned by the EXISTING `/api/social/workflows/{id}/results` endpoint
 * (`ContentWorkflowResult` on the frontend, `ContentWorkflowResultResponse` on the backend). This is
 * the only HTTP surface that already runs `IContentDiscoveryService` end to end, so the shortlist
 * reuses it rather than adding a new discovery endpoint. Declared structurally here (not imported
 * from `lib/api/types`) so this module stays framework-free and independently testable.
 */
export interface DiscoveredWorkflowResult {
  id: string;
  title: string;
  networkSource: string;
  videoUrl: string;
  thumbnailUrl: string;
  views: number;
  likes: number;
  comments: number;
}

export interface RankedDiscoveryCandidate extends DiscoveryShortlistCandidate {
  title: string;
  networkSource: string;
  thumbnailUrl?: string;
  views: number | null;
  likes: number | null;
  comments: number | null;
}

/**
 * Projects the existing workflow results endpoint's items onto the shortlist candidate shape. Pure
 * mapping, no I/O — the caller is responsible for starting/polling the workflow session.
 */
export function mapDiscoveryResultsToShortlistCandidates(
  results: readonly DiscoveredWorkflowResult[]
): RankedDiscoveryCandidate[] {
  return results.map((result) => ({
    id: result.id,
    contentUrl: result.videoUrl,
    title: result.title,
    networkSource: result.networkSource,
    thumbnailUrl: result.thumbnailUrl || undefined,
    views: result.views,
    likes: result.likes,
    comments: result.comments,
  }));
}

/**
 * A checklist group/item as returned by the EXISTING `/api/integrations/organization` overview
 * endpoint (`OrganizationIntegrationsOverview.checklistGroups`). Declared structurally for the same
 * framework-free-testability reason as above.
 */
export interface ChecklistGroupLike {
  key: string;
  items: readonly { key: string; isCompleted: boolean }[];
}

/** Prefix of the per-network niche-search checklist items emitted by the overview endpoint. */
const NICHE_SEARCH_ITEM_PREFIX = "single-analyzer-niche-search-actor-";

/**
 * Niche-search readiness for ONE network:
 * - `ready` — an actor resolves; the stage will run.
 * - `unconfigured` — the network has a working adapter but no actor is routed or mapped yet.
 * - `unsupported` — no adapter exists for this network (Instagram, YouTube today). The backend emits
 *   NO checklist item for such a network precisely so the UI can say "not supported yet" instead of
 *   sending the operator to configure an actor that could never be dispatched.
 */
export type NicheSearchReadiness = "ready" | "unconfigured" | "unsupported";

/**
 * Reads the per-network niche-search readiness out of the EXISTING `/api/integrations/organization`
 * checklist (INT-DISC-03) — still no new backend endpoint. This is how the shortlist distinguishes
 * "the niche stage is unconfigured" from "it ran and found nothing" (CD-13/D12).
 *
 * `networkKey` may be empty, which is the UI's "all networks" selection: readiness is then the best
 * outcome across every supported network, because discovery only needs one network to produce
 * candidates.
 */
export function getNicheSearchReadiness(
  checklistGroups: readonly ChecklistGroupLike[],
  networkKey: string
): NicheSearchReadiness {
  const group = checklistGroups.find((candidate) => candidate.key === "single-analyzer");
  const items = (group?.items ?? []).filter((candidate) => candidate.key.startsWith(NICHE_SEARCH_ITEM_PREFIX));

  const relevant = networkKey
    ? items.filter((candidate) => candidate.key === `${NICHE_SEARCH_ITEM_PREFIX}${networkKey.toLowerCase()}`)
    : items;

  if (relevant.length === 0) {
    return "unsupported";
  }

  return relevant.some((candidate) => candidate.isCompleted) ? "ready" : "unconfigured";
}

/**
 * The niche criterion is the only one gated on a configurable actor (references/own reuse the
 * already-configured scraping credentials). Returns the operator-facing reason the stage cannot run,
 * or `null` when it can. Pure function so the banner never depends on a network call having happened.
 */
export function getNicheSearchBlockedMessage(
  criterion: string,
  readiness: NicheSearchReadiness,
  networkLabel?: string
): string | null {
  if (criterion !== "niche" || readiness === "ready") {
    return null;
  }

  const network = networkLabel?.trim() || "This network";

  return readiness === "unsupported"
    ? `${network} niche search isn't supported yet — only TikTok has a working niche-search adapter today. Pick TikTok, or use the reference/own account criteria.`
    : `${network} niche search isn't configured yet. Map or route a niche-search actor for it under Settings / Integrations.`;
}

/** Convenience predicate: is the niche stage blocked for this criterion and readiness? */
export function isNicheSearchBlocked(criterion: string, readiness: NicheSearchReadiness): boolean {
  return criterion === "niche" && readiness !== "ready";
}

// ── discovery run outcome (start + poll) ─────────────────────────────────────
//
// Discovery no longer finishes inside the POST that starts it: a real scrape takes minutes, and the
// browser/proxy aborted the blocking request at ~30 s while the Apify actor kept running and billing.
// The screen now starts a session and polls `/api/social/workflows/{id}/status` until it reports a
// terminal state. These pure functions are the whole decision layer for that loop.

/** Backend status values that mean the session will not change again. */
const TERMINAL_DISCOVERY_STATUSES = new Set(["completed", "failed", "unconfigured"]);

/**
 * What the operator is actually looking at. `empty`, `failed` and `unconfigured` are kept apart on
 * purpose: they have three different fixes, and rendering a provider failure as "No candidates found
 * yet" told the operator to change their niche when the real problem was the provider.
 */
export type DiscoveryRunOutcome = "running" | "candidates" | "empty" | "failed" | "unconfigured";

/** The fields of `ContentWorkflowStatus` this decision actually depends on. */
export interface DiscoveryStatusLike {
  status: string;
  totalResults: number;
}

/** True once the status endpoint reports a state the background task will not move on from. */
export function isDiscoveryTerminal(status: string): boolean {
  return TERMINAL_DISCOVERY_STATUSES.has(status);
}

/**
 * An unrecognised status is treated as still running, never as a terminal failure: inventing a
 * failure the backend never reported would be the same class of lie this change removes.
 */
export function resolveDiscoveryOutcome(status: DiscoveryStatusLike): DiscoveryRunOutcome {
  if (status.status === "failed") {
    return "failed";
  }

  if (status.status === "unconfigured") {
    return "unconfigured";
  }

  if (status.status === "completed") {
    return status.totalResults > 0 ? "candidates" : "empty";
  }

  return "running";
}

/** Operator-facing explanation, or `null` when the shortlist itself is the answer. */
export function getDiscoveryOutcomeMessage(outcome: DiscoveryRunOutcome): string | null {
  switch (outcome) {
    case "failed":
      return "Discovery could not be completed — the provider could not be reached or rejected the run. Nothing is wrong with your search criterion; try again, and check the niche-search log if it repeats.";
    case "unconfigured":
      return "Discovery is not configured for the selected network. Map or route a niche-search actor under Settings / Integrations.";
    case "empty":
      return "Discovery ran and found no candidates for this client and criterion.";
    default:
      return null;
  }
}

/** Only an unconfigured stage has a settings screen that would fix it. */
export function discoveryOutcomeOffersSettingsLink(outcome: DiscoveryRunOutcome): boolean {
  return outcome === "unconfigured";
}

/** One step row as reported by the status endpoint. */
export interface DiscoveryStepLike {
  status: string;
}

/**
 * 0-based index of the active row for the progress overlay. A terminal session returns
 * `steps.length`, which is the overlay's own convention for "no row is still pending".
 */
export function discoveryStepIndex(steps: readonly DiscoveryStepLike[], status: string): number {
  if (isDiscoveryTerminal(status)) {
    return steps.length;
  }

  const active = steps.findIndex((step) => step.status === "in_progress");
  return active === -1 ? 0 : active;
}

/**
 * Selecting a candidate ALWAYS replaces the current selection, never adds to it — the shortlist is a
 * single-selection surface by construction, not a multi-select list with a size-1 validation rule
 * bolted on. Selecting the already-selected candidate again clears the selection (toggle-off), which
 * keeps the UI predictable without needing a separate "clear" affordance.
 */
export function selectSingleCandidate(
  currentSelectedId: string | null,
  candidateId: string
): string | null {
  return currentSelectedId === candidateId ? null : candidateId;
}

/**
 * Builds the exact payload the EXISTING single-analysis start endpoint already accepts. Returns
 * `null` when nothing is selected or the selected id no longer matches a known candidate — the caller
 * must not fabricate a request in that case.
 */
export function buildAnalyzeSelectedCandidatePayload(
  candidates: readonly DiscoveryShortlistCandidate[],
  selectedId: string | null,
  clientId?: string
): { url: string; clientId?: string } | null {
  if (!selectedId) {
    return null;
  }

  const candidate = candidates.find((item) => item.id === selectedId);
  if (!candidate) {
    return null;
  }

  return clientId ? { url: candidate.contentUrl, clientId } : { url: candidate.contentUrl };
}
