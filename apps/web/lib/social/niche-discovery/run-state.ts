/**
 * Interprets a `DiscoveryRun` status for the route/client components (ND-09, design §10).
 *
 * `Requested`/`Running`/`Completed`/`PartiallyCompleted`/`Failed` must always render distinctly.
 * `PartiallyCompleted` is explicitly flagged as degraded so no caller can present it as a full
 * result, and `Failed` always carries its structured reason key instead of invented text.
 */

export type DiscoveryRunStatus = "Requested" | "Running" | "Completed" | "PartiallyCompleted" | "Failed";

export type RunStateTone = "neutral" | "success" | "warning" | "error";

export interface RunStateDescription {
  status: DiscoveryRunStatus;
  label: string;
  tone: RunStateTone;
  isDegraded: boolean;
  failureReasonKey: string | null;
}

const LABELS: Record<DiscoveryRunStatus, string> = {
  Requested: "Requested",
  Running: "Running",
  Completed: "Completed",
  PartiallyCompleted: "Partially completed",
  Failed: "Failed",
};

const TONES: Record<DiscoveryRunStatus, RunStateTone> = {
  Requested: "neutral",
  Running: "neutral",
  Completed: "success",
  PartiallyCompleted: "warning",
  Failed: "error",
};

export function isRunPollable(status: string): boolean {
  return status === "Requested" || status === "Running";
}

export function isRunViewable(status: string): boolean {
  return status === "Completed" || status === "PartiallyCompleted";
}

export function isRunDegraded(status: string): boolean {
  return status === "PartiallyCompleted";
}

export function describeRunState(status: string, failureReasonKey: string | null): RunStateDescription {
  const knownStatus = (status in LABELS ? status : "Requested") as DiscoveryRunStatus;

  return {
    status: knownStatus,
    label: LABELS[knownStatus],
    tone: TONES[knownStatus],
    isDegraded: isRunDegraded(knownStatus),
    failureReasonKey: knownStatus === "Failed" ? failureReasonKey : null,
  };
}
