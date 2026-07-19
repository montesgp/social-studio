// Pure formatting/derivation helpers for the sysadmin content-generation audit UI
// (job index + timeline detail). Kept side-effect free so duration/status logic can be
// unit tested without a DOM or network layer — see content-generation-audit-format.test.ts.

/** Renders an elapsed-milliseconds duration as a compact human-readable string. */
export function formatElapsedMs(elapsedMs: number): string {
  if (!Number.isFinite(elapsedMs) || elapsedMs < 0) {
    return "—";
  }

  if (elapsedMs < 1000) {
    return `${Math.round(elapsedMs)}ms`;
  }

  return formatDurationSeconds(elapsedMs / 1000);
}

/** Renders a duration given in seconds (nullable — jobs that never finished have none). */
export function formatDurationSeconds(totalSeconds: number | null | undefined): string {
  if (totalSeconds === null || totalSeconds === undefined || !Number.isFinite(totalSeconds)) {
    return "—";
  }

  const seconds = Math.max(0, Math.round(totalSeconds));
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const remainingSeconds = seconds % 60;

  if (hours > 0) {
    return `${hours}h ${minutes}m`;
  }
  if (minutes > 0) {
    return `${minutes}m ${remainingSeconds}s`;
  }
  return `${remainingSeconds}s`;
}

const FAILED_STATUSES = new Set(["Failed"]);
const PARTIAL_STATUSES = new Set(["Partial", "PartialSuccess"]);
const IN_PROGRESS_STATUSES = new Set(["Pending", "Queued", "Running", "AnalyzingProfile"]);

export type JobStatusTone = "success" | "warning" | "destructive" | "info" | "muted";

/** True when a job's terminal status represents an outright failure. */
export function isFailedStatus(status: string): boolean {
  return FAILED_STATUSES.has(status);
}

/** True when a job's terminal status represents a partial outcome (some, not all, formats persisted). */
export function isPartialStatus(status: string): boolean {
  return PARTIAL_STATUSES.has(status);
}

/** True when a job has not yet reached a terminal status. */
export function isInProgressStatus(status: string): boolean {
  return IN_PROGRESS_STATUSES.has(status);
}

/**
 * Maps a job status to a badge tone. Callers must still pair the tone with an icon/text
 * label (never color alone) so the failure/partial state reads for color-blind operators.
 */
export function getJobStatusTone(status: string): JobStatusTone {
  if (isFailedStatus(status)) return "destructive";
  if (isPartialStatus(status)) return "warning";
  if (isInProgressStatus(status)) return "info";
  if (status === "Completed") return "success";
  return "muted";
}

/** True when a timeline step's provider attempt was retried at least once. */
export function hadRetry(step: { attemptNumber: number; retryCount: number }): boolean {
  return step.retryCount > 0 || step.attemptNumber > 1;
}

export type SlideOutcomeClass = "succeeded" | "degraded" | "failed" | "unknown";

/**
 * Classifies a slide-outcome step's outcome string into a coarse bucket for styling.
 * The backend does not publish an enum for this field (plain string on the wire), so we
 * match defensively on common substrings rather than an exhaustive allowlist.
 */
export function classifySlideOutcome(slideOutcome: string | null | undefined): SlideOutcomeClass {
  if (!slideOutcome) {
    return "unknown";
  }

  const normalized = slideOutcome.toLowerCase();
  if (normalized.includes("fail")) {
    return "failed";
  }
  if (normalized.includes("css") || normalized.includes("degrad")) {
    return "degraded";
  }
  if (normalized.includes("succeed") || normalized.includes("success") || normalized.includes("ok")) {
    return "succeeded";
  }
  return "unknown";
}

/** Formats a byte count as a compact human-readable size (KB/MB). */
export function formatBytes(sizeBytes: number): string {
  if (!Number.isFinite(sizeBytes) || sizeBytes < 0) {
    return "—";
  }
  if (sizeBytes < 1024) {
    return `${sizeBytes} B`;
  }
  const kb = sizeBytes / 1024;
  if (kb < 1024) {
    return `${kb.toFixed(1)} KB`;
  }
  const mb = kb / 1024;
  return `${mb.toFixed(1)} MB`;
}
