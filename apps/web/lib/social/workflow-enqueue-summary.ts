/** Mirrors the backend's `EnqueuedCandidateResult` (Application/Features/Social/Workflow/Enqueue). */
export interface EnqueuedCandidateResult {
  candidateId: string;
  accepted: boolean;
  rejectionReason: string | null;
}

/** Mirrors the backend's `EnqueueContentWorkflowCandidatesResponse`. */
export interface EnqueueWorkflowCandidatesResponse {
  workflowId: string;
  candidates: EnqueuedCandidateResult[];
  estimatedMinutes: number;
  jobsUrl: string;
}

export interface EnqueueFeedbackSummary {
  acceptedCount: number;
  rejectedCount: number;
  totalCount: number;
  /** Human-readable ETA line, e.g. "Estimated 12 minutes". Spec: "no synchronous poll" — this is the
   * one-shot post-enqueue message, not a live status. */
  etaMessage: string;
  /** Directs the operator to the content-bank (jobs) screen for the actual generation results. */
  jobsLinkLabel: string;
}

/**
 * Pure formatter for the post-enqueue feedback panel (spec: "Post-enqueue feedback" requirement).
 * Kept dependency-free so it can be unit tested with the repo's `tsx --test` runner; the
 * `hooks/use-workflow-enqueue.ts` hook is the only caller.
 */
export function summarizeEnqueueResponse(response: EnqueueWorkflowCandidatesResponse): EnqueueFeedbackSummary {
  const acceptedCount = response.candidates.filter((c) => c.accepted).length;
  const rejectedCount = response.candidates.length - acceptedCount;

  return {
    acceptedCount,
    rejectedCount,
    totalCount: response.candidates.length,
    etaMessage: formatEta(response.estimatedMinutes),
    jobsLinkLabel: "View enqueue progress"
  };
}

export function formatEta(estimatedMinutes: number): string {
  if (estimatedMinutes <= 0) {
    return "Estimated time: a few minutes";
  }
  if (estimatedMinutes === 1) {
    return "Estimated time: about 1 minute";
  }
  if (estimatedMinutes < 60) {
    return `Estimated time: about ${estimatedMinutes} minutes`;
  }

  const hours = Math.floor(estimatedMinutes / 60);
  const minutes = estimatedMinutes % 60;
  const hoursLabel = `${hours} hour${hours === 1 ? "" : "s"}`;
  if (minutes === 0) {
    return `Estimated time: about ${hoursLabel}`;
  }
  return `Estimated time: about ${hoursLabel} ${minutes} min`;
}
