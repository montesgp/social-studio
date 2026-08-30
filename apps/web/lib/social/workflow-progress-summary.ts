import type { ContentWorkflowCandidateStatus } from "@/lib/api/types";

/**
 * The operator-facing progress bucket for the new Generator-owned progress view — coarser than
 * `describeCandidateStatus` (workflow-candidate-status.ts), which this module deliberately does not
 * duplicate: `deriveProgressBucket` groups the same backend `WorkflowCandidateStatus` enum values into
 * the four buckets the progress page's summary counters need (pending / working / completed / failed).
 */
export type WorkflowProgressBucket = "pending" | "working" | "completed" | "failed";

const BUCKET_BY_STATUS: Record<string, WorkflowProgressBucket> = {
  Queued: "pending",
  Analyzing: "working",
  BatchQueued: "working",
  BatchStarted: "completed",
  AnalysisFailed: "failed",
  BatchFailed: "failed"
};

/** An unknown status falls back to "pending" rather than throwing — same fail-open stance as
 * `describeCandidateStatus`. */
export function deriveProgressBucket(status: string): WorkflowProgressBucket {
  return BUCKET_BY_STATUS[status] ?? "pending";
}

export interface WorkflowProgressSummary {
  pending: number;
  working: number;
  completed: number;
  failed: number;
  total: number;
}

/** Pure counter over a workflow's candidates, used for the progress page's summary row. */
export function summarizeWorkflowProgress(
  candidates: readonly ContentWorkflowCandidateStatus[]
): WorkflowProgressSummary {
  const summary: WorkflowProgressSummary = { pending: 0, working: 0, completed: 0, failed: 0, total: 0 };

  for (const candidate of candidates) {
    summary[deriveProgressBucket(candidate.status)] += 1;
    summary.total += 1;
  }

  return summary;
}
