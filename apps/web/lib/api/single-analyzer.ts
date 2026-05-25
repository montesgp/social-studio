// Client for the backend single-analyzer pipeline.
// The Next.js route handler for scraping has been removed (CAP5-MUST-01/03).
// Apify is called server-side — no token or Apify domain is reachable from the browser.

import { apiRequest } from "@/lib/api/client";
import type { SingleContentAnalysisResponse, SingleAnalyzerContentSnapshot, SingleAnalyzerAnalysis } from "@/lib/api/types";
import type { SingleAnalyzerResult } from "@/lib/mocks/single-analyzer";

export type { SingleContentAnalysisResponse };

export interface AnalyzeSingleContentPayload {
  url: string;
  clientId?: string;
}

/**
 * Call the backend analyze endpoint (POST /api/social/single-analyzer/analyze).
 * This is ONE-SHOT: scrape + AI analysis + persist happens on the server side.
 * The browser never contacts Apify directly.
 */
export async function analyzeSingleContent(
  payload: AnalyzeSingleContentPayload,
  token?: string
): Promise<SingleContentAnalysisResponse> {
  return apiRequest<SingleContentAnalysisResponse>(
    "/api/social/single-analyzer/analyze",
    {
      method: "POST",
      body: JSON.stringify(payload),
    },
    token
  );
}

/**
 * Map the backend `SingleContentAnalysisResponse` to the UI-local `SingleAnalyzerResult` shape.
 * Keeps the existing component shape stable so the UI requires minimal changes.
 */
export function mapBackendResponseToResult(
  response: SingleContentAnalysisResponse,
  organizationId: string
): SingleAnalyzerResult {
  const snapshot: SingleAnalyzerContentSnapshot = response.snapshot;
  const analysis: SingleAnalyzerAnalysis = response.analysis;

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
      snapshot.thumbnailUrl ??
      `https://picsum.photos/seed/${encodeURIComponent(response.sourceUrl)}/640/360`,
    videoUrl:
      snapshot.mediaReferences.find((r) => r.kind === "video")?.uri ??
      snapshot.canonicalUrl,
    title: snapshot.title ?? "Untitled",
    author: snapshot.author ?? "Unknown creator",
    views: snapshot.metrics["views"] ?? 0,
    likes: snapshot.metrics["likes"] ?? 0,
    comments: snapshot.metrics["comments"] ?? 0,
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
    contentType: snapshot.contentType ?? undefined,
    shares: snapshot.metrics["shares"] ?? 0,
    saves: snapshot.metrics["saves"] ?? 0,
    durationSeconds: snapshot.durationSeconds ?? null,
    publishedAtUtc: snapshot.publishedAtUtc,
    hashtags: snapshot.tags ?? [],
    dataSource: "apify",
  };
}
