// ── Content-generation package progress (overlay) ──────────────────────────────
// Extracted from components/social/single-analyzer-client.tsx so packageProgressState is
// unit-testable via the tsx --test runner (issue #67, BAPA-STATUS-03).

/** Total number of user-visible package generation steps (Queued → Preparing → AI → Render → Zip). */
export const PACKAGE_STEP_COUNT = 5;

/**
 * Delay (ms) a poll loop should wait before making its next status request.
 *
 * The FIRST poll attempt (attemptIndex === 0) must fire immediately — the overlay is already
 * visible by the time polling starts, so there is nothing to gain from sleeping before the very
 * first status check. Every subsequent attempt waits the normal interval between requests.
 */
export function pollAttemptDelayMs(attemptIndex: number, intervalMs: number): number {
  return attemptIndex > 0 ? intervalMs : 0;
}

function isAiCheckpointStatus(status: string): boolean {
  return (
    status === "AiSucceeded" ||
    status === "AiFailed" ||
    status === "Rendered" ||
    status === "RenderFailed" ||
    status === "Succeeded" ||
    status === "Failed"
  );
}

function isRenderCheckpointStatus(status: string): boolean {
  return (
    status === "Rendered" ||
    status === "RenderFailed" ||
    status === "Succeeded" ||
    status === "Failed"
  );
}

export interface PackageProgressState {
  percent: number;
  label: string;
  currentStep: number;
  completedFormats: number;
}

export function packageProgressState(
  status: string | null,
  perFormatStatus: Record<string, string | undefined>,
  totalFormats: number
): PackageProgressState {
  const total = Math.max(totalFormats, 1);
  const statuses = Object.values(perFormatStatus).filter((s): s is string => Boolean(s));
  const aiDone = statuses.filter(isAiCheckpointStatus).length;
  const renderDone = statuses.filter(isRenderCheckpointStatus).length;

  if (!status) {
    return {
      percent: 12,
      label: "Initializing generation request",
      currentStep: 1,
      completedFormats: 0,
    };
  }

  if (status === "Queued") {
    return { percent: 8, label: "Queued", currentStep: 0, completedFormats: 0 };
  }

  // issue #67 (BAPA-STATUS-03): first-time ClientAesthetic generation blocks on the enqueued
  // profile analysis before any slide/format work starts. Placed BEFORE the "Running" branch —
  // folded into the "Preparing context" step (currentStep: 1, same as Running's first sub-phase)
  // per design's Open Question resolution, so no PACKAGE_STEPS renumbering is needed elsewhere.
  // All later steps stay pending/blocked until the status transitions away from AnalyzingProfile.
  if (status === "AnalyzingProfile") {
    return {
      percent: 14,
      label: "Analyzing profile aesthetic",
      currentStep: 1,
      completedFormats: 0,
    };
  }

  if (status === "Running") {
    if (aiDone === 0 && renderDone === 0) {
      return {
        percent: 18,
        label: "Preparing analysis context",
        currentStep: 1,
        completedFormats: 0,
      };
    }

    if (renderDone === 0) {
      const percent = Math.round(22 + (Math.min(aiDone, total) / total) * 36);
      return {
        percent,
        label: `Generating formats with AI (${Math.min(aiDone, total)}/${total})`,
        currentStep: 2,
        completedFormats: 0,
      };
    }

    if (renderDone < total) {
      const percent = Math.round(60 + (renderDone / total) * 30);
      return {
        percent,
        label: `Rendering assets (${renderDone}/${total})`,
        currentStep: 3,
        completedFormats: renderDone,
      };
    }

    return {
      percent: 95,
      label: "Building package.zip",
      currentStep: 4,
      completedFormats: total,
    };
  }

  if (status === "Completed") {
    return {
      percent: 100,
      label: "Requested formats generated",
      currentStep: PACKAGE_STEP_COUNT,
      completedFormats: total,
    };
  }

  if (status === "Partial" || status === "PartialSuccess") {
    return {
      percent: 100,
      label: "Completed with partial results",
      currentStep: PACKAGE_STEP_COUNT,
      completedFormats: Math.min(renderDone, total),
    };
  }

  return {
    percent: 100,
    label: "Generation failed",
    currentStep: Math.min(4, PACKAGE_STEP_COUNT - 1),
    completedFormats: Math.min(renderDone, total),
  };
}

export function generationHistoryLabels(hasPackage: boolean, status: string): string[] {
  if (!hasPackage) {
    switch (status) {
      case "Completed": return ["Generated"];
      case "Partial":
      case "PartialSuccess": return ["Partial"];
      case "Failed": return ["Generation failed"];
      case "Running": return ["Generating…"];
      case "Pending":
      case "Queued": return ["Queued"];
      default: return [status];
    }
  }

  switch (status) {
    case "Pending":
    case "Queued": return ["Generated", "Regeneration queued"];
    case "Running": return ["Generated", "Regenerating…"];
    case "Partial":
    case "PartialSuccess": return ["Generated", "Regeneration partial"];
    case "Failed": return ["Generated", "Regeneration failed"];
    default: return ["Generated"];
  }
}

export function shouldAutoSwitchToPackage(input: {
  activeJobId: string | null;
  observedJobId: string | null;
  userStartedJobId: string | null;
  alreadySwitchedJobId: string | null;
  status: string | null;
}): boolean {
  return Boolean(
    input.activeJobId &&
    input.observedJobId === input.activeJobId &&
    input.userStartedJobId === input.activeJobId &&
    input.alreadySwitchedJobId !== input.activeJobId &&
    (input.status === "Completed" || input.status === "Partial" || input.status === "PartialSuccess")
  );
}

export function shouldRefreshTerminalHistory(input: {
  activeJobId: string | null;
  observedJobId: string | null;
  status: string | null;
  lastRefreshKey: string | null;
}): boolean {
  if (!input.activeJobId || input.observedJobId !== input.activeJobId) return false;
  if (
    input.status !== "Completed" &&
    input.status !== "Partial" &&
    input.status !== "PartialSuccess" &&
    input.status !== "Failed"
  ) return false;

  return input.lastRefreshKey !== `${input.activeJobId}:${input.status}`;
}
