/**
 * Mirrors `WorkflowCandidateStatus.BatchStarted` (Domain/Social/Workflow) — the honest "generation was
 * launched for this candidate" signal (not "content is fully rendered", see the enum's own docs). Once a
 * candidate reaches this status the candidate grid moves it out of the main "Candidate results" view into
 * its Processed tab, per the operator's request to stop re-browsing candidates already sent to
 * generation. `Recovered` candidates are explicitly brought back by the operator and therefore stay (or
 * return to) the active pool even though generation already happened for them once.
 */
const PROCESSED_STATUS = "BatchStarted";

export function isProcessedCandidateStatus(status: string): boolean {
  return status === PROCESSED_STATUS;
}

export interface CandidateGrouping<T extends { status: string }> {
  active: T[];
  processed: T[];
}

/** Pure split of one page's candidate results into the active pool and the processed-and-hidden set. */
export function splitCandidatesByProcessedState<T extends { status: string }>(
  results: readonly T[]
): CandidateGrouping<T> {
  const active: T[] = [];
  const processed: T[] = [];

  for (const result of results) {
    if (isProcessedCandidateStatus(result.status)) {
      processed.push(result);
    } else {
      active.push(result);
    }
  }

  return { active, processed };
}
