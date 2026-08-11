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

/**
 * Whether the operator has already configured a TikTok niche-search actor (the
 * `single-analyzer-niche-search-actor` checklist item, INT-DISC-03). This is how the shortlist
 * distinguishes "the niche stage is unconfigured" from "it ran and found nothing" (CD-13/D12)
 * WITHOUT a new backend endpoint: `ContentDiscoveryPlan.UnconfiguredSourceKeys` is not surfaced onto
 * the workflow session response, but the same fact is already exposed through the existing
 * integrations checklist.
 */
export function isSingleAnalyzerNicheSearchActorConfigured(
  checklistGroups: readonly ChecklistGroupLike[]
): boolean {
  const group = checklistGroups.find((candidate) => candidate.key === "single-analyzer");
  const item = group?.items.find((candidate) => candidate.key === "single-analyzer-niche-search-actor");
  return item?.isCompleted ?? false;
}

/**
 * The niche criterion is the only one gated on a configurable actor (references/own reuse the
 * already-configured scraping credentials). Pure predicate so the "go configure it" banner never
 * depends on a network call having already happened.
 */
export function isNicheSearchUnconfigured(
  criterion: string,
  nicheSearchActorConfigured: boolean
): boolean {
  return criterion === "niche" && !nicheSearchActorConfigured;
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
