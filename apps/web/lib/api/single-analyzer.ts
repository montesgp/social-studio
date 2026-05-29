// Client for the backend single-analyzer pipeline.
// The Next.js route handler for scraping has been removed (CAP5-MUST-01/03).
// Apify is called server-side — no token or Apify domain is reachable from the browser.

import { apiRequest } from "@/lib/api/client";
import type {
  SingleAnalyzerStatusResponse,
  SingleAnalyzerContentSnapshot,
  SingleAnalyzerAnalysis,
  SingleContentAnalysisHistoryResponse,
} from "@/lib/api/types";
import type { SingleAnalyzerResult } from "@/lib/mocks/single-analyzer";

export type { SingleAnalyzerStatusResponse, SingleContentAnalysisHistoryResponse };

export interface AnalyzeSingleContentPayload {
  url: string;
  clientId?: string;
}

/**
 * Start an analysis (POST /api/social/single-analyzer/analyze).
 * Returns immediately with status=Requested + the analysis id. The heavy scrape + AI run
 * happens in the background — poll {@link getSingleContentAnalysis} until Analyzed or Failed.
 */
export async function analyzeSingleContent(
  payload: AnalyzeSingleContentPayload,
  token?: string
): Promise<SingleAnalyzerStatusResponse> {
  return apiRequest<SingleAnalyzerStatusResponse>(
    "/api/social/single-analyzer/analyze",
    {
      method: "POST",
      body: JSON.stringify(payload),
    },
    token
  );
}

/**
 * Poll the current status/result of an analysis (GET /api/social/single-analyzer/analyze/{id}).
 */
export async function getSingleContentAnalysis(
  id: string,
  token?: string
): Promise<SingleAnalyzerStatusResponse> {
  return apiRequest<SingleAnalyzerStatusResponse>(
    `/api/social/single-analyzer/analyze/${encodeURIComponent(id)}`,
    { method: "GET" },
    token
  );
}

/**
 * Re-run ONLY the AI stage of an existing analysis. Reuses the persisted snapshot from the
 * previous run — does NOT hit the extraction provider (no extra Apify tokens). The returned
 * status has Status="Analyzing"; poll {@link getSingleContentAnalysis} to wait for terminal.
 */
export async function retrySingleContentAnalysisAi(
  id: string,
  token?: string
): Promise<SingleAnalyzerStatusResponse> {
  return apiRequest<SingleAnalyzerStatusResponse>(
    `/api/social/single-analyzer/analyze/${encodeURIComponent(id)}/retry-ai`,
    { method: "POST" },
    token
  );
}

/**
 * List the current user's saved analyses for the active org (paged, newest first).
 * Used by the history grid below the analyzer so the user can review past work or resume
 * without re-running Apify + Claude.
 */
export async function listSingleContentAnalyses(
  page = 1,
  pageSize = 20,
  token?: string
): Promise<SingleContentAnalysisHistoryResponse> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return apiRequest<SingleContentAnalysisHistoryResponse>(
    `/api/social/single-analyzer/analyze?${params.toString()}`,
    { method: "GET" },
    token
  );
}

/**
 * Hard-delete a saved analysis (owner-scoped). The backend cascades to all linked
 * content-generation jobs, format-results, packages, and the physical ZIP files.
 */
export async function deleteSingleContentAnalysis(id: string, token?: string): Promise<void> {
  await apiRequest<void>(
    `/api/social/single-analyzer/analyze/${encodeURIComponent(id)}`,
    { method: "DELETE" },
    token
  );
}

/**
 * Map a completed backend status response to the UI-local `SingleAnalyzerResult` shape.
 * Must only be called when `response.status === "Analyzed"` — snapshot/analysis are required.
 */
export function mapBackendResponseToResult(
  response: SingleAnalyzerStatusResponse,
  organizationId: string
): SingleAnalyzerResult {
  const snapshot: SingleAnalyzerContentSnapshot | null = response.snapshot;
  const analysis: SingleAnalyzerAnalysis | null = response.analysis;

  return {
    id: response.id,
    organizationId,
    clientId: response.clientId ?? null,
    clientName: null, // not returned by the analyze endpoint; enriched client-side if needed
    inputMode: "url" as const,
    inputUrl: response.sourceUrl,
    searchQuery: "",
    networkSource: response.platformKey,
    thumbnailUrl:
      snapshot?.thumbnailUrl ??
      `https://picsum.photos/seed/${encodeURIComponent(response.sourceUrl)}/640/360`,
    videoUrl:
      snapshot?.mediaReferences.find((r) => r.kind === "video")?.uri ??
      snapshot?.canonicalUrl ??
      response.sourceUrl,
    title: snapshot?.title ?? "Untitled",
    author: snapshot?.author ?? "Unknown creator",
    views: snapshot?.metrics["views"] ?? 0,
    likes: snapshot?.metrics["likes"] ?? 0,
    comments: snapshot?.metrics["comments"] ?? 0,
    strategyMode: "",
    // Combine AI analysis into a single readable block for the existing videoAnalysis field.
    videoAnalysis: analysis
      ? [
          analysis.summary,
          "",
          `Likely audience: ${analysis.likelyAudience}`,
          `Narrative: ${analysis.narrativeStructure}`,
          "",
          analysis.strengths.length > 0
            ? `Strengths:\n${analysis.strengths.map((s) => `• ${s}`).join("\n")}`
            : "",
          analysis.weaknesses.length > 0
            ? `Weaknesses:\n${analysis.weaknesses.map((w) => `• ${w}`).join("\n")}`
            : "",
        ]
          .filter(Boolean)
          .join("\n")
      : "",
    scriptDraft: analysis?.derivedIdeas?.[0]?.concept ?? "",
    analyzedAtUtc: response.completedAtUtc ?? response.updatedAtUtc,
    downloadedAtUtc: null,
    contentType: snapshot?.contentType ?? undefined,
    shares: snapshot?.metrics["shares"] ?? 0,
    saves: snapshot?.metrics["saves"] ?? 0,
    durationSeconds: snapshot?.durationSeconds ?? null,
    publishedAtUtc: snapshot?.publishedAtUtc ?? null,
    hashtags: snapshot?.tags ?? [],
    dataSource: "apify",
  };
}
