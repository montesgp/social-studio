"use client";

/**
 * ND-09 / design §11 — `CrossPlatformValidation` is permanently unavailable in this change (it has
 * no data source). This note tells the operator WHY the denominator changed rather than letting
 * them silently trust a score computed from fewer signals: the excluded signal is named and the
 * remaining weights were renormalized, not zero-filled.
 */

import type { ExcludedSignal } from "@/lib/api/niche-discovery";

interface ExcludedSignalsNoteProps {
  excludedSignals: ExcludedSignal[];
  effectiveWeightDenominator: number;
}

export function ExcludedSignalsNote({ excludedSignals, effectiveWeightDenominator }: ExcludedSignalsNoteProps) {
  if (excludedSignals.length === 0) {
    return null;
  }

  return (
    <div
      className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-xs text-amber-700 dark:text-amber-300"
      data-testid="excluded-signals-note"
    >
      <p className="font-semibold">
        {excludedSignals.length} signal{excludedSignals.length === 1 ? "" : "s"} excluded from this score
      </p>
      <ul className="mt-1 list-disc space-y-0.5 pl-4">
        {excludedSignals.map((signal) => (
          <li key={signal.key}>
            <span className="font-medium">{signal.key}</span> — {signal.reasonKey}
          </li>
        ))}
      </ul>
      <p className="mt-1">
        Remaining weights were renormalized to sum to 1.0 (effective denominator:{" "}
        {effectiveWeightDenominator.toFixed(2)}) — this score is not computed from fewer signals silently.
      </p>
    </div>
  );
}
