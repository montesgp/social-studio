/**
 * Turns one persisted run into a row the operator can choose from.
 *
 * A discovery run costs provider quota, AI completions and review time, and its full result runs to
 * hundreds of rows — so the history shows a summary and the operator opens the one worth reading. Each
 * row has to answer two questions on its own: which run was this, and is it worth reopening.
 */

import type { DiscoveryRunHistoryEntry } from "@/lib/api/niche-discovery";

/** Beyond this the terms stop being a label and start being a paragraph. */
const MAX_VISIBLE_SEED_TERMS = 3;

const OPENABLE_STATUSES = new Set(["Completed", "PartiallyCompleted"]);

export interface DescribedHistoryEntry {
  /** What the run was asked to do — the only thing separating two otherwise identical rows. */
  intent: string;
  isOpenable: boolean;
  /** Non-null when the result carries a caveat the operator must not lose on the way in. */
  warning: string | null;
}

export function describeHistoryEntry(entry: DiscoveryRunHistoryEntry): DescribedHistoryEntry {
  return {
    intent: describeIntent(entry.seedTerms),
    isOpenable: OPENABLE_STATUSES.has(entry.status),
    warning: describeWarning(entry),
  };
}

function describeIntent(seedTerms: string[]): string {
  if (seedTerms.length === 0) {
    return "Open discovery — no terms given";
  }

  const visible = seedTerms.slice(0, MAX_VISIBLE_SEED_TERMS).join(", ");
  const hidden = seedTerms.length - MAX_VISIBLE_SEED_TERMS;

  return hidden > 0 ? `${visible} +${hidden} more` : visible;
}

function describeWarning(entry: DiscoveryRunHistoryEntry): string | null {
  if (entry.failureReasonKey) {
    return `Failed: ${entry.failureReasonKey}`;
  }

  // The run still ranked, but its niche NAMES came from the lexical fallback rather than the semantic
  // layer. That changes how much the labels can be trusted, so it travels with the row.
  if (entry.semanticDegraded) {
    return "Partial: niche labels came from the lexical fallback, not the semantic layer";
  }

  return null;
}
