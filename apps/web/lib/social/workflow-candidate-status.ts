/**
 * Pure formatter for the per-candidate enqueue-sequence status surfaced on the workflow history detail
 * (verify-report CRITICAL gap: "Post-enqueue feedback" must be readable after reload, not just from
 * ephemeral post-enqueue React state). Mirrors the backend's
 * `WorkflowCandidateStatus` enum (Domain/Social/Workflow) by name — `Queued`, `Analyzing`,
 * `AnalysisFailed`, `BatchQueued`, `BatchFailed`, `BatchStarted`.
 */
export type WorkflowCandidateStatusTone = "pending" | "success" | "failed";

export interface WorkflowCandidateStatusPresentation {
  label: string;
  tone: WorkflowCandidateStatusTone;
  isTerminal: boolean;
}

const STATUS_PRESENTATION: Record<string, WorkflowCandidateStatusPresentation> = {
  Queued: { label: "Queued", tone: "pending", isTerminal: false },
  Analyzing: { label: "Analyzing", tone: "pending", isTerminal: false },
  AnalysisFailed: { label: "Analysis failed", tone: "failed", isTerminal: true },
  BatchQueued: { label: "Batch queued", tone: "pending", isTerminal: false },
  BatchFailed: { label: "Batch failed", tone: "failed", isTerminal: true },
  BatchStarted: { label: "Batch started", tone: "success", isTerminal: true }
};

/** An unknown status renders as-is with a neutral tone rather than throwing — the backend enum can
 * grow without a frontend round-trip becoming a hard failure. */
export function describeCandidateStatus(status: string): WorkflowCandidateStatusPresentation {
  return STATUS_PRESENTATION[status] ?? { label: status, tone: "pending", isTerminal: false };
}

/** Copy shown next to any non-terminal candidate status. There is no live per-candidate ETA on the
 * history read surface (ETA is a one-shot number returned only by the enqueue response) — this is an
 * honest qualitative message, not a fabricated number. */
export const CANDIDATE_STATUS_CHECK_BACK_MESSAGE =
  "Still processing — this can take a few minutes. Check back later.";
