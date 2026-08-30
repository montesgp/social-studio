"use client";

/**
 * The organization's discovery-run history. A run costs provider quota, AI completions and review time,
 * and before this its result was only reachable while the browser tab stayed open — closing the tool
 * lost the way back to work that had already been paid for.
 *
 * Summary only, on purpose: the opportunity list and its evidence run to hundreds of rows, so this
 * answers "which run" and the operator opens the one worth reading.
 */

import { useEffect, useState } from "react";
import { AlertTriangle, History, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { describeRunState, type RunStateTone } from "@/lib/social/niche-discovery/run-state";
import { describeHistoryEntry } from "@/lib/social/niche-discovery/run-history";
import { formatMarketLabel } from "@/lib/social/niche-discovery/market-label";
import { listDiscoveryRuns, type DiscoveryRunHistoryEntry } from "@/lib/api/niche-discovery";

const TONE_TO_BADGE_VARIANT: Record<RunStateTone, BadgeVariant> = {
  neutral: "muted",
  success: "success",
  warning: "warning",
  error: "destructive",
};

interface RunHistoryListProps {
  token?: string;
  /** Bumped by the parent after a run starts, so the list picks it up without a page reload. */
  refreshKey?: number;
  onOpenRun: (runId: string) => void;
}

export function RunHistoryList({ token, refreshKey, onOpenRun }: RunHistoryListProps) {
  const [runs, setRuns] = useState<DiscoveryRunHistoryEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;

    let cancelled = false;

    listDiscoveryRuns(undefined, token)
      .then((response) => {
        if (!cancelled) setRuns(response.runs);
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : "Could not load your run history.");
      });

    return () => {
      cancelled = true;
    };
  }, [token, refreshKey]);

  if (error) {
    return <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-4 text-sm">{error}</div>;
  }

  if (runs === null) {
    return (
      <div className="flex items-center gap-2 rounded-xl border border-border bg-background/30 p-4 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" />
        Loading your run history…
      </div>
    );
  }

  if (runs.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-background/30 p-4 text-sm text-muted-foreground">
        No discovery runs yet. The ones you start are kept here.
      </div>
    );
  }

  return (
    <div data-testid="run-history">
      <h2 className="flex items-center gap-2 text-sm font-semibold">
        <History className="h-4 w-4" />
        Previous runs
      </h2>
      <ul className="mt-3 space-y-2">
        {runs.map((entry) => (
          <HistoryRow key={entry.runId} entry={entry} onOpenRun={onOpenRun} />
        ))}
      </ul>
    </div>
  );
}

function HistoryRow({
  entry,
  onOpenRun,
}: {
  entry: DiscoveryRunHistoryEntry;
  onOpenRun: (runId: string) => void;
}) {
  const described = describeHistoryEntry(entry);
  const state = describeRunState(entry.status, entry.failureReasonKey);

  return (
    <li
      className="flex flex-wrap items-start justify-between gap-3 rounded-xl border border-border bg-background/40 p-3"
      data-testid="run-history-row"
    >
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={TONE_TO_BADGE_VARIANT[state.tone]}>{state.label}</Badge>
          <span className="text-sm font-medium">{described.intent}</span>
        </div>

        <p className="mt-1 text-xs text-muted-foreground">
          {marketLabel(entry.sourceMarketCode)} → {marketLabel(entry.targetMarketCode)} · {entry.contentFormat} ·{" "}
          {entry.windowDays}d · {entry.depth} · {entry.providerUnitsSpent} provider units
        </p>

        <p className="mt-0.5 text-xs text-muted-foreground">{formatTimestamp(entry.createdAtUtc)}</p>

        {described.warning ? (
          <p
            className="mt-1 flex items-start gap-1 text-xs text-amber-600 dark:text-amber-500"
            data-testid="run-history-warning"
          >
            <AlertTriangle className="mt-0.5 h-3 w-3 shrink-0" />
            {described.warning}
          </p>
        ) : null}
      </div>

      {described.isOpenable ? (
        <Button variant="outline" size="sm" onClick={() => onOpenRun(entry.runId)}>
          Open results
        </Button>
      ) : null}
    </li>
  );
}

/** The history stores market CODES; the selector's own labelling keeps both surfaces consistent. */
function marketLabel(code: string): string {
  return formatMarketLabel({ code, displayNameKey: "", primaryLanguage: code.split("-")[0] ?? "" });
}

function formatTimestamp(iso: string): string {
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? iso : parsed.toLocaleString();
}
