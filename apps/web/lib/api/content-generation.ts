// API client for the content-generation pipeline (single-analyzer package generation).

import { apiRequest } from "@/lib/api/client";
import { ACTIVE_ORG_STORAGE_KEY } from "@/lib/auth/session";
import type {
  ContentFormatKey,
  ContentGenerationJobStatusResponse,
  ContentGenerationJobStatus,
  GeneratePackageRequest,
  GeneratePackageResponse,
  GeneratedPackageManifest,
  AnalysisPackagesResponse,
} from "@/lib/api/types";

export type {
  ContentFormatKey,
  ContentGenerationJobStatusResponse,
  ContentGenerationJobStatus,
  GeneratePackageRequest,
  GeneratePackageResponse,
  GeneratedPackageManifest,
  AnalysisPackagesResponse,
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

function parseContentDispositionFilename(contentDisposition: string | null): string | null {
  if (!contentDisposition) return null;
  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return utf8Match[1];
    }
  }

  const fallbackMatch = /filename="?([^\";]+)"?/i.exec(contentDisposition);
  return fallbackMatch?.[1] ?? null;
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

// ─── Assemble-on-demand types ─────────────────────────────────────────────────

/** Per-client selection for the assemble endpoint. Omit `formatKeys` to include all stored formats. */
export interface AssembleClientSelection {
  clientId: string;
  formatKeys?: string[];
}

/** Request body for POST .../package/assemble */
export interface AssemblePackageRequest {
  clients?: AssembleClientSelection[];
}

/**
 * POST /api/social/single-analyzer/{analysisId}/package/assemble
 * Assembles a ZIP on demand from the currently-stored per-asset rows (INC-3+ durable pipeline).
 * Returns the zip blob + Content-Disposition filename.
 * Use this for NEW-model packages (manifest.has_stored_assets === true).
 */
export async function assemblePackage(
  analysisId: string,
  selection?: AssemblePackageRequest,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<{ blob: Blob; filename: string }> {
  const base = typeof window !== "undefined" ? "/hive-api" : "";
  const headers = new Headers({ "Content-Type": "application/json" });

  if (opts?.token) {
    headers.set("Authorization", `Bearer ${opts.token}`);
  }

  if (typeof window !== "undefined") {
    const orgId = window.localStorage.getItem(ACTIVE_ORG_STORAGE_KEY);
    if (orgId) {
      headers.set("X-Tenant-Id", orgId);
    }
  }

  const response = await fetch(
    `${base}/api/social/single-analyzer/${encodeURIComponent(analysisId)}/package/assemble`,
    {
      method: "POST",
      headers,
      body: JSON.stringify(selection ?? {}),
      signal: opts?.signal,
      cache: "no-store",
    }
  );

  if (!response.ok) {
    let message = `Failed to assemble package (HTTP ${response.status})`;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string };
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // ignore: keep default HTTP message
    }
    throw new Error(message);
  }

  const filename =
    parseContentDispositionFilename(response.headers.get("Content-Disposition")) ??
    `content-package-${analysisId}.zip`;
  const blob = await response.blob();
  return { blob, filename };
}

// ─── Download PPTX ───────────────────────────────────────────────────────────

/**
 * GET /api/social/single-analyzer/{analysisId}/formats/{formatKey}/download-pptx?jobId={jobId}
 *
 * Downloads the persisted format composition spec as an editable PPTX file.
 * Requires the jobId that produced the format result (available from the generation job state).
 * Returns a Blob with MIME type application/vnd.openxmlformats-officedocument.presentationml.presentation.
 *
 * Throws when:
 * - 409: the format predates B3 markup persistence (not-exportable)
 * - 404: job or format not found
 * - Any other non-2xx HTTP status
 */
export async function downloadFormatAsPptx(
  analysisId: string,
  formatKey: string,
  jobId: string,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<{ blob: Blob; filename: string }> {
  const base = typeof window !== "undefined" ? "/hive-api" : "";
  const headers = new Headers();

  if (opts?.token) {
    headers.set("Authorization", `Bearer ${opts.token}`);
  }

  if (typeof window !== "undefined") {
    const orgId = window.localStorage.getItem(ACTIVE_ORG_STORAGE_KEY);
    if (orgId) {
      headers.set("X-Tenant-Id", orgId);
    }
  }

  const url = `${base}/api/social/single-analyzer/${encodeURIComponent(analysisId)}/formats/${encodeURIComponent(formatKey)}/download-pptx?jobId=${encodeURIComponent(jobId)}`;

  const response = await fetch(url, {
    method: "GET",
    headers,
    signal: opts?.signal,
    cache: "no-store",
  });

  if (!response.ok) {
    let message = `Failed to download PPTX (HTTP ${response.status})`;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string };
      if (response.status === 409) {
        message = problem.detail ?? "This format cannot be exported as PPTX (markup not persisted).";
      } else {
        message = problem.detail ?? problem.title ?? message;
      }
    } catch {
      // ignore: keep default HTTP message
    }
    throw new Error(message);
  }

  const filename =
    parseContentDispositionFilename(response.headers.get("Content-Disposition")) ??
    `${formatKey}.pptx`;
  const blob = await response.blob();
  return { blob, filename };
}

// ─── Regenerate / Delete format types ────────────────────────────────────────

export interface RegenerateFormatRequest {
  clientId: string;
  objection?: string;
}

export interface RegeneratedAssetInfo {
  storagePath: string;
  filename: string;
  mime: string;
  sizeBytes: number;
}

export interface RegenerateFormatResponse {
  formatKey: string;
  assets: RegeneratedAssetInfo[];
}

/**
 * POST /api/social/single-analyzer/{analysisId}/formats/{formatKey}/regenerate
 * Re-renders a single format and overwrites the stored asset(s).
 * Returns the updated asset metadata.
 */
export async function regenerateFormat(
  analysisId: string,
  formatKey: string,
  payload: RegenerateFormatRequest,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<RegenerateFormatResponse> {
  return apiRequest<RegenerateFormatResponse>(
    `/api/social/single-analyzer/${encodeURIComponent(analysisId)}/formats/${encodeURIComponent(formatKey)}/regenerate`,
    {
      method: "POST",
      body: JSON.stringify(payload),
      signal: opts?.signal,
    },
    opts?.token
  );
}

/**
 * DELETE /api/social/single-analyzer/{analysisId}/formats/{formatKey}?clientId=...
 * Physically deletes a format's asset object(s) from storage and removes the DB rows.
 */
export async function deleteFormatItem(
  analysisId: string,
  formatKey: string,
  clientId: string,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<void> {
  return apiRequest<void>(
    `/api/social/single-analyzer/${encodeURIComponent(analysisId)}/formats/${encodeURIComponent(formatKey)}?clientId=${encodeURIComponent(clientId)}`,
    {
      method: "DELETE",
      signal: opts?.signal,
    },
    opts?.token
  );
}

/**
 * GET /api/social/single-analyzer/{analysisId}/packages
 * Returns the per-client package tree (formats + assets) for the given analysis.
 * Sourced from DB — survives page refresh.
 * INC-6: drives the multi-client Packages tab tree.
 */
export async function listAnalysisPackages(
  analysisId: string,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<AnalysisPackagesResponse> {
  return apiRequest<AnalysisPackagesResponse>(
    `/api/social/single-analyzer/${encodeURIComponent(analysisId)}/packages`,
    {
      method: "GET",
      signal: opts?.signal,
    },
    opts?.token
  );
}

// ─── Export to Canva ─────────────────────────────────────────────────────────

/** Response shape returned by POST .../export-to-canva on success. */
export interface ExportToCanvaResult {
  designId: string;
  editUrl: string;
  expiresAt: string; // ISO 8601
}

/**
 * POST /api/social/single-analyzer/{analysisId}/formats/{formatKey}/export-to-canva
 *
 * Exports the persisted format composition to Canva via the Design Import API.
 * Returns the Canva edit URL on success.
 *
 * Throws when:
 * - 409 with code "canva.not_connected": user has not connected their Canva account.
 *   The caller should start the OAuth Connect flow.
 * - 409 with code "canva.reconnect_required": the stored token was revoked — reconnect.
 * - 409 with code "content-gen.not-exportable": format predates B3 markup persistence.
 * - 502: Canva API failure (timeout, api-failure).
 * - Any other non-2xx HTTP status.
 *
 * The thrown Error will have a `code` property when the server returns a problem+json body.
 */
export async function exportFormatToCanva(
  analysisId: string,
  formatKey: string,
  jobId: string,
  orgIntegrationId: string,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<ExportToCanvaResult> {
  const base = typeof window !== "undefined" ? "/hive-api" : "";
  const headers = new Headers({ "Content-Type": "application/json" });

  if (opts?.token) {
    headers.set("Authorization", `Bearer ${opts.token}`);
  }

  if (typeof window !== "undefined") {
    const orgId = window.localStorage.getItem(ACTIVE_ORG_STORAGE_KEY);
    if (orgId) {
      headers.set("X-Tenant-Id", orgId);
    }
  }

  const url = `${base}/api/social/single-analyzer/${encodeURIComponent(analysisId)}/formats/${encodeURIComponent(formatKey)}/export-to-canva`;

  const response = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify({ jobId, orgIntegrationId }),
    signal: opts?.signal,
    cache: "no-store",
  });

  if (!response.ok) {
    let message = `Failed to export to Canva (HTTP ${response.status})`;
    let code: string | undefined;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string; extensions?: { code?: string }; code?: string };
      code = problem.code ?? (problem.extensions as Record<string, string> | undefined)?.["code"];
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // ignore: keep default HTTP message
    }
    const err = new Error(message) as Error & { code?: string; status?: number };
    err.code = code;
    err.status = response.status;
    throw err;
  }

  return response.json() as Promise<ExportToCanvaResult>;
}

/**
 * GET /api/social/content-generation-jobs/{jobId}/package
 * Downloads the generated ZIP using auth + tenant headers.
 */
export async function downloadPackageFile(
  jobId: string,
  opts?: { token?: string; signal?: AbortSignal }
): Promise<{ blob: Blob; filename: string }> {
  const base = typeof window !== "undefined" ? "/hive-api" : "";
  const headers = new Headers();

  if (opts?.token) {
    headers.set("Authorization", `Bearer ${opts.token}`);
  }

  if (typeof window !== "undefined") {
    const orgId = window.localStorage.getItem(ACTIVE_ORG_STORAGE_KEY);
    if (orgId) {
      headers.set("X-Tenant-Id", orgId);
    }
  }

  const response = await fetch(
    `${base}/api/social/content-generation-jobs/${encodeURIComponent(jobId)}/package`,
    {
      method: "GET",
      headers,
      signal: opts?.signal,
      cache: "no-store",
    }
  );

  if (!response.ok) {
    let message = `Failed to download package (HTTP ${response.status})`;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string };
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // ignore: keep default HTTP message
    }
    throw new Error(message);
  }

  const filename =
    parseContentDispositionFilename(response.headers.get("Content-Disposition")) ??
    `content-package-${jobId}.zip`;
  const blob = await response.blob();
  return { blob, filename };
}
