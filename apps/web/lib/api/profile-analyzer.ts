// Typed API client for the ProfileAnalyzer backend pipeline.
// Routes served by MapProfileAnalyzerEndpoints.cs:
//   POST /api/social/profile-analyzer/analyze          → analyzeProfile
//   GET  /api/social/profile-analyzer/analyze/{id}     → getProfileAnalysis (poll)
//   GET  /api/social/profile-analyzer/analyze/{id}/dashboard  → getProfileDashboard
//   POST /api/social/profile-analyzer/analyze/{id}/impute     → imputeProfileAnalysis
//   GET  /api/social/profile-analyzer/analyze          → listProfileAnalyses
//   DELETE /api/social/profile-analyzer/analyze/{id}   → deleteProfileAnalysis
//   GET  /api/social/clients/{clientId}/own-accounts?network=instagram → listInstagramOwnAccounts

import { apiRequest } from "@/lib/api/client";
import type {
  ProfileAnalysisStatusResponse,
  ProfileDashboardResponse,
  ProfileAnalysisHistoryResponse,
  ImputeProfileAnalysisRequest,
  OwnAccountItem,
} from "@/lib/api/types/profile-analyzer";

export type { ProfileAnalysisStatusResponse, ProfileDashboardResponse, ProfileAnalysisHistoryResponse };

// ── Request payload ────────────────────────────────────────────────────────────

export interface AnalyzeProfilePayload {
  /** Client-path: the client id. Null/undefined for URL-path analyses. */
  clientId?: string | null;
  /** Client-path: the own Instagram account id. Null/undefined for URL-path analyses. */
  ownAccountId?: string | null;
  /** URL-path: the Instagram profile URL. Null/undefined for client-path analyses. */
  profileUrl?: string | null;
}

// ── API functions ──────────────────────────────────────────────────────────────

/**
 * Start a profile analysis (POST /api/social/profile-analyzer/analyze).
 * Returns immediately with status=Requested + the analysis id.
 * Poll {@link getProfileAnalysis} until Analyzed or Failed.
 */
export async function analyzeProfile(
  payload: AnalyzeProfilePayload,
  token?: string
): Promise<ProfileAnalysisStatusResponse> {
  return apiRequest<ProfileAnalysisStatusResponse>(
    "/api/social/profile-analyzer/analyze",
    {
      method: "POST",
      body: JSON.stringify(payload),
    },
    token
  );
}

/**
 * Poll the current status / result of a profile analysis
 * (GET /api/social/profile-analyzer/analyze/{id}).
 */
export async function getProfileAnalysis(
  id: string,
  token?: string
): Promise<ProfileAnalysisStatusResponse> {
  return apiRequest<ProfileAnalysisStatusResponse>(
    `/api/social/profile-analyzer/analyze/${encodeURIComponent(id)}`,
    { method: "GET" },
    token
  );
}

/**
 * Fetch the full dashboard projection for a completed profile analysis
 * (GET /api/social/profile-analyzer/analyze/{id}/dashboard).
 */
export async function getProfileDashboard(
  id: string,
  token?: string
): Promise<ProfileDashboardResponse> {
  return apiRequest<ProfileDashboardResponse>(
    `/api/social/profile-analyzer/analyze/${encodeURIComponent(id)}/dashboard`,
    { method: "GET" },
    token
  );
}

/**
 * Impute AI-detected fields into the linked client's BrandManual/ContentPlan/GeneralStrategy
 * (POST /api/social/profile-analyzer/analyze/{id}/impute).
 */
export async function imputeProfileAnalysis(
  id: string,
  req: ImputeProfileAnalysisRequest,
  token?: string
): Promise<void> {
  await apiRequest<void>(
    `/api/social/profile-analyzer/analyze/${encodeURIComponent(id)}/impute`,
    {
      method: "POST",
      body: JSON.stringify(req),
    },
    token
  );
}

/**
 * List profile analyses for the active org, newest first (paged).
 * (GET /api/social/profile-analyzer/analyze)
 */
export async function listProfileAnalyses(
  page = 1,
  token?: string
): Promise<ProfileAnalysisHistoryResponse> {
  const params = new URLSearchParams({ page: String(page), pageSize: "20" });
  return apiRequest<ProfileAnalysisHistoryResponse>(
    `/api/social/profile-analyzer/analyze?${params.toString()}`,
    { method: "GET" },
    token
  );
}

/**
 * Hard-delete a profile analysis (DELETE /api/social/profile-analyzer/analyze/{id}).
 */
export async function deleteProfileAnalysis(
  id: string,
  token?: string
): Promise<void> {
  await apiRequest<void>(
    `/api/social/profile-analyzer/analyze/${encodeURIComponent(id)}`,
    { method: "DELETE" },
    token
  );
}

/**
 * Re-run ONLY the AI stage of an existing profile analysis. Reuses the persisted
 * scrape snapshot — does NOT re-scrape the profile. Returns immediately with
 * status=Analyzing; poll {@link getProfileAnalysis} until Analyzed or Failed.
 * (POST /api/social/profile-analyzer/analyze/{id}/retry-ai)
 */
export async function retryProfileAnalysis(
  id: string,
  token?: string
): Promise<ProfileAnalysisStatusResponse> {
  return apiRequest<ProfileAnalysisStatusResponse>(
    `/api/social/profile-analyzer/analyze/${encodeURIComponent(id)}/retry-ai`,
    { method: "POST" },
    token
  );
}

/**
 * List a client's own accounts filtered to Instagram
 * (GET /api/social/clients/{clientId}/own-accounts?network=instagram).
 * Used by the ProfileSourcePicker to show the own-account selector.
 */
export async function listInstagramOwnAccounts(
  clientId: string,
  token?: string
): Promise<OwnAccountItem[]> {
  const params = new URLSearchParams({ network: "instagram" });
  const response = await apiRequest<{ items: OwnAccountItem[] }>(
    `/api/social/clients/${encodeURIComponent(clientId)}/own-accounts?${params.toString()}`,
    { method: "GET" },
    token
  );
  return response.items;
}
