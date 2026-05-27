// API client for the content-generation pipeline (single-analyzer package generation).

import { apiRequest } from "@/lib/api/client";
import type {
  ContentFormatKey,
  ContentGenerationJobStatusResponse,
  ContentGenerationJobStatus,
  GeneratePackageRequest,
  GeneratePackageResponse,
  GeneratedPackageManifest,
} from "@/lib/api/types";

export type {
  ContentFormatKey,
  ContentGenerationJobStatusResponse,
  ContentGenerationJobStatus,
  GeneratePackageRequest,
  GeneratePackageResponse,
  GeneratedPackageManifest,
};

/** All 7 supported format keys — used when the user wants to generate all formats. */
export const ALL_FORMAT_KEYS: ContentFormatKey[] = [
  "carousel",
  "flyer",
  "reel-script",
  "content-idea",
  "cta-variation",
  "caption",
  "hook",
];

/** Human-readable labels for each format key. */
export const FORMAT_LABELS: Record<ContentFormatKey, string> = {
  carousel: "Carousel",
  flyer: "Flyer",
  "reel-script": "Reel Script",
  "content-idea": "Content Idea",
  "cta-variation": "CTA Variation",
  caption: "Caption",
  hook: "Hook",
};

// ── Raw backend response shape (before client-side transform) ─────────────────

interface RawJobStatusResponse {
  jobId: string;
  status: ContentGenerationJobStatus;
  perFormatStatus: Record<string, string>;
  manifestJson: string | null;
}

function parseManifest(manifestJson: string | null): GeneratedPackageManifest | null {
  if (!manifestJson) return null;
  try {
    return JSON.parse(manifestJson) as GeneratedPackageManifest;
  } catch {
    return null;
  }
}

function normalizeStatusResponse(raw: RawJobStatusResponse): ContentGenerationJobStatusResponse {
  return {
    jobId: raw.jobId,
    status: raw.status,
    perFormatStatus: raw.perFormatStatus as ContentGenerationJobStatusResponse["perFormatStatus"],
    manifest: parseManifest(raw.manifestJson),
  };
}

// ── API functions ─────────────────────────────────────────────────────────────

/**
 * POST /api/social/single-analyzer/{analysisId}/generate-package
 * Starts an async content package generation job.
 * Returns immediately (202) with the jobId and a status poll URL.
 */
export async function requestPackage(
  analysisId: string,
  payload: GeneratePackageRequest,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<GeneratePackageResponse> {
  return apiRequest<GeneratePackageResponse>(
    `/api/social/single-analyzer/${encodeURIComponent(analysisId)}/generate-package`,
    {
      method: "POST",
      body: JSON.stringify(payload),
      signal: opts?.signal,
    },
    opts?.token
  );
}

/**
 * GET /api/social/content-generation-jobs/{jobId}/status
 * Polls the current status of a generation job.
 */
export async function getJobStatus(
  jobId: string,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<ContentGenerationJobStatusResponse> {
  const raw = await apiRequest<RawJobStatusResponse>(
    `/api/social/content-generation-jobs/${encodeURIComponent(jobId)}/status`,
    {
      method: "GET",
      signal: opts?.signal,
    },
    opts?.token
  );
  return normalizeStatusResponse(raw);
}

/**
 * Returns the download URL and suggested filename for a completed package.
 * The caller is responsible for triggering the download (via <a download> or fetch+blob).
 * The URL hits the streaming endpoint that returns application/zip.
 */
export function getPackageDownloadUrl(
  jobId: string
): { url: string; filename: string } {
  // Uses the same base URL path that apiRequest resolves — in-browser requests go through /hive-api
  const base = typeof window !== "undefined" ? "/hive-api" : "";
  return {
    url: `${base}/api/social/content-generation-jobs/${encodeURIComponent(jobId)}/package`,
    filename: `content-package-${jobId}.zip`,
  };
}
