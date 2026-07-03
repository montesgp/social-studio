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
