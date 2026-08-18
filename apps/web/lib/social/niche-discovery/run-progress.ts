/**
 * What to show the operator WHILE a discovery run is working.
 *
 * A run is a background job with no visible surface, so the screen has to narrate it. Two rules come
 * straight from run e6124e47 (2026-08-15), where the worker threw mid-append and left the run in
 * Running: the phase must be named rather than implied by a spinner, and a run that has plainly
 * stopped moving must SAY so instead of animating forever — otherwise a crash reads as a slow job and
 * the operator waits for something that will never arrive.
 *
 * The percentage is deliberately coarse and never reaches 100 while working: it is a sign of life, not
 * a measurement. The backend reports no step count, and inventing a precise-looking number from
 * elapsed time would be a lie with a decimal point.
 */

/** Past this, a still-Running run is treated as stalled rather than slow. */
export const STALLED_AFTER_MS = 3 * 60 * 1000;

export interface RunProgressInput {
  status: string;
  elapsedMs: number;
  providerUnitsSpent: number;
}

export interface RunProgress {
  isBlocking: boolean;
  isStalled: boolean;
  title: string;
  description: string;
  percent: number;
}

const TERMINAL_STATUSES = new Set(["Completed", "PartiallyCompleted", "Failed"]);

export function describeRunProgress({ status, elapsedMs, providerUnitsSpent }: RunProgressInput): RunProgress {
  if (TERMINAL_STATUSES.has(status)) {
    return { isBlocking: false, isStalled: false, title: "", description: "", percent: 100 };
  }

  if (status === "Requested") {
    return {
      isBlocking: true,
      isStalled: false,
      title: "Discovery run queued",
      description: "Waiting in the queue for a worker to pick it up…",
      percent: 5,
    };
  }

  const isStalled = elapsedMs > STALLED_AFTER_MS;

  if (isStalled) {
    return {
      isBlocking: true,
      isStalled: true,
      title: "Discovery run is taking longer than expected",
      description:
        "The run has not reported progress for a while. It may have stopped — you can keep waiting, " +
        "or close this and start a new run.",
      percent: 90,
    };
  }

  // Provider units are only spent once sourcing actually calls out, so they are the one honest marker
  // the backend already reports for "past the fetching phase".
  const isSourcing = providerUnitsSpent === 0;

  return {
    isBlocking: true,
    isStalled: false,
    title: "Discovering niches",
    description: isSourcing
      ? "Harvesting what is already performing, sourcing channels and videos…"
      : "Grouping the results into niches and scoring them…",
    percent: isSourcing ? 25 : 65,
  };
}
