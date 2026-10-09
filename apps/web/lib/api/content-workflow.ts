import { apiRequest } from "@/lib/api/client";
import {
  ContentWorkflowCandidateStatus,
  ContentWorkflowHistory,
  ContentWorkflowHistoryItem,
  ContentWorkflowResult,
  ContentWorkflowSearchCriterion,
  ContentWorkflowSelection,
  ContentWorkflowSession,
  ContentWorkflowStatus,
  ContentWorkflowStrategyMode,
  EnqueueCandidateCompositionPayload,
  EnqueueWorkflowCandidatesResponse,
  PageResult,
  SocialClient
} from "@/lib/api/types";

function organizationHeaders(organizationId: string) {
  return {
    "X-Tenant-Id": organizationId
  };
}

export interface StartContentWorkflowPayload {
  clientId: string;
  clientName: string;
  searchCriterion: ContentWorkflowSearchCriterion;
  strategyMode: ContentWorkflowStrategyMode;
  networkSources: string[];
  /**
   * The operator's EXPLICIT opt-in to a billed provider run. Omitted or `false` means "serve from the
   * stored candidate bank" — the backend defaults it to `false`, so a caller can never start a paid
   * scrape by forgetting the field.
   */
  forceRefresh?: boolean;
  /**
   * Where the run's raw material comes from. `discovery` (the backend default when omitted) reaches
   * the market: scraping providers or the stored candidate bank. `ai-ideas` reaches nothing — the
   * model invents topics from the client's own context, so no actor runs and nothing is billed.
   *
   * Only the RAW MATERIAL differs. The client's aesthetic is applied later, at generation, and is
   * identical either way.
   */
  originMode?: "discovery" | "ai-ideas";
  /**
   * Caps how many candidates a single discovery/idea-draw run collects (operator's "Max results" input).
   * Omitted falls back to the backend's default pool size — unchanged behaviour for older callers.
   */
  maxResults?: number;
}

export async function listWorkflowClients(organizationId: string, token?: string) {
  const params = new URLSearchParams({ page: "1", pageSize: "100", includeInactive: "false" });
  return apiRequest<PageResult<SocialClient>>(
    `/api/social/clients?${params.toString()}`,
    { headers: organizationHeaders(organizationId) },
    token
  );
}

export async function startContentWorkflow(organizationId: string, payload: StartContentWorkflowPayload, token?: string) {
  return apiRequest<ContentWorkflowSession>(
    "/api/social/workflows/content-generation",
    {
      method: "POST",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify(payload)
    },
    token
  );
}

export async function getContentWorkflowStatus(organizationId: string, workflowId: string, token?: string) {
  return apiRequest<ContentWorkflowStatus>(
    `/api/social/workflows/${workflowId}/status`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function listContentWorkflowResults(
  organizationId: string,
  workflowId: string,
  page: number,
  pageSize: number,
  token?: string
) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });

  return apiRequest<PageResult<ContentWorkflowResult>>(
    `/api/social/workflows/${workflowId}/results?${params.toString()}`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function selectContentWorkflowResults(
  organizationId: string,
  workflowId: string,
  resultIds: string[],
  token?: string
) {
  return apiRequest<ContentWorkflowSelection>(
    `/api/social/workflows/${workflowId}/select`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify({ resultIds })
    },
    token
  );
}

/**
 * `POST /api/social/workflows/{workflowId}/enqueue` — per-candidate format composition submitted in one
 * call. The backend fans this out into independent analysis-then-batch sequences (one queue item per
 * candidate); the response is the ONLY feedback the screen renders — no polling. See design decision #3
 * ("no synchronous poll") and #5 (one endpoint, N candidates).
 */
export async function enqueueWorkflowCandidates(
  organizationId: string,
  workflowId: string,
  clientId: string,
  candidates: EnqueueCandidateCompositionPayload[],
  token?: string
) {
  return apiRequest<EnqueueWorkflowCandidatesResponse>(
    `/api/social/workflows/${workflowId}/enqueue`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify({ clientId, candidates })
    },
    token
  );
}

export async function getContentWorkflowHistory(organizationId: string, workflowId: string, token?: string) {
  return apiRequest<ContentWorkflowHistory>(
    `/api/social/workflows/${workflowId}/history`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

/**
 * Operator's explicit "bring it back" action on a candidate the candidate grid moved into its Processed
 * section (see `ContentWorkflowResult.status`). Only legal from `BatchStarted` — the backend rejects any
 * other source status with a conflict.
 */
export async function recoverContentWorkflowCandidate(
  organizationId: string,
  workflowId: string,
  candidateId: string,
  token?: string
) {
  return apiRequest<ContentWorkflowCandidateStatus>(
    `/api/social/workflows/${workflowId}/candidates/${candidateId}/recover`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function listContentWorkflowHistory(
  organizationId: string,
  limit = 12,
  token?: string,
  clientId?: string
) {
  const params = new URLSearchParams({ limit: String(limit) });
  if (clientId) {
    params.set("clientId", clientId);
  }
  return apiRequest<ContentWorkflowHistoryItem[]>(
    `/api/social/workflows/history?${params.toString()}`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}
