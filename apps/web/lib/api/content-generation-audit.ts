import { apiRequest } from "@/lib/api/client";
import { ApiError, ContentGenerationJobListResponse, ContentGenerationJobTimeline } from "@/lib/api/types";

/** GET /api/platform/content-generation-jobs — bounded, most-recent-first job index. */
export async function listContentGenerationJobs(
  query: { page: number; pageSize: number },
  token?: string
): Promise<ContentGenerationJobListResponse> {
  const params = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize)
  });
  return apiRequest<ContentGenerationJobListResponse>(
    `/api/platform/content-generation-jobs?${params.toString()}`,
    undefined,
    token
  );
}

/**
 * GET /api/platform/content-generation-jobs/{jobId}/timeline — full per-job audit timeline.
 * Returns null when the job does not exist (backend 404), so callers can render a clear
 * "unknown job" message instead of treating it as a request failure.
 */
export async function getContentGenerationJobTimeline(
  jobId: string,
  token?: string
): Promise<ContentGenerationJobTimeline | null> {
  try {
    return await apiRequest<ContentGenerationJobTimeline>(
      `/api/platform/content-generation-jobs/${encodeURIComponent(jobId)}/timeline`,
      undefined,
      token
    );
  } catch (err) {
    const apiError = err as ApiError;
    if (apiError.problem?.status === 404 || apiError.message === "HTTP 404") {
      return null;
    }
    throw err;
  }
}
