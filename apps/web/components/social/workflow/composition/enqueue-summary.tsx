"use client";

import Link from "next/link";
import { AlertTriangle, CheckCircle2, Loader2, Send, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FORMAT_LABELS } from "@/lib/api/content-generation";
import type { ContentWorkflowResult, EnqueueWorkflowCandidatesResponse } from "@/lib/api/types";
import type { CompositionState, CompositionValidationResult } from "@/lib/social/workflow-composition";
import { getCandidateComposition } from "@/lib/social/workflow-composition";
import { summarizeEnqueueResponse } from "@/lib/social/workflow-enqueue-summary";

export interface EnqueueSummaryProps {
  selectedResults: ContentWorkflowResult[];
  composition: CompositionState;
  validation: CompositionValidationResult;
  busy: boolean;
  response: EnqueueWorkflowCandidatesResponse | null;
  error: string | null;
  onEnqueue: () => void;
  onReset: () => void;
}

/**
 * candidate × chosen-formats preview (spec: "Compose formats per candidate" — nothing enqueued until the
 * operator confirms here) plus the one-shot post-enqueue feedback (spec: "Post-enqueue feedback" — status
 * per candidate + ETA + link to the content-bank/jobs screen, no polling).
 */
export function EnqueueSummary({
  selectedResults,
  composition,
  validation,
  busy,
  response,
  error,
  onEnqueue,
  onReset
}: EnqueueSummaryProps) {
  if (response) {
    const summary = summarizeEnqueueResponse(response);
    return (
      <div className="space-y-3">
        <div className="rounded-xl border border-emerald-500/40 bg-emerald-500/10 p-3 text-sm">
          <p className="font-medium">
            {summary.acceptedCount} of {summary.totalCount} candidates queued
          </p>
          <p className="mt-1 text-xs text-muted-foreground">{summary.etaMessage}</p>
        </div>
        <ul className="space-y-1">
          {response.candidates.map((candidate) => (
            <li
              key={candidate.candidateId}
              className="flex items-center justify-between rounded-lg border border-border/70 bg-background/40 px-3 py-2 text-xs"
            >
              <span className="flex items-center gap-2">
                {candidate.accepted ? (
                  <CheckCircle2 className="h-3.5 w-3.5 text-emerald-400" />
                ) : (
                  <XCircle className="h-3.5 w-3.5 text-red-400" />
                )}
                {candidate.candidateId}
              </span>
              {!candidate.accepted && candidate.rejectionReason && (
                <span className="text-muted-foreground">{candidate.rejectionReason}</span>
              )}
            </li>
          ))}
        </ul>
        <div className="flex flex-wrap items-center gap-2">
          <Button asChild size="sm">
            <Link href={response.jobsUrl || "/content-generation-jobs"}>
              <Send className="h-4 w-4" /> {summary.jobsLinkLabel}
            </Link>
          </Button>
          <Button size="sm" variant="outline" onClick={onReset}>
            Compose another batch
          </Button>
        </div>
      </div>
    );
  }

  if (selectedResults.length === 0) {
    return <p className="text-sm text-muted-foreground">Select one or more candidates to compose their formats.</p>;
  }

  return (
    <div className="space-y-3">
      {selectedResults.map((result) => {
        const entries = getCandidateComposition(composition, result.id);
        const isEmpty = entries.length === 0;
        return (
          <div key={result.id} className="rounded-xl border border-border bg-background/40 p-3">
            <p className="truncate text-sm font-medium">{result.title}</p>
            {isEmpty ? (
              <p className="mt-1 flex items-center gap-1.5 text-xs text-amber-400">
                <AlertTriangle className="h-3.5 w-3.5" /> No format chosen yet
              </p>
            ) : (
              <div className="mt-1 flex flex-wrap gap-1.5">
                {entries.map((entry) => (
                  <Badge key={entry.formatKey} variant="muted" className="text-[10px]">
                    {FORMAT_LABELS[entry.formatKey]} × {entry.count}
                  </Badge>
                ))}
              </div>
            )}
          </div>
        );
      })}

      {error && <p className="text-sm text-red-300">{error}</p>}

      <Button disabled={busy || !validation.valid} onClick={onEnqueue}>
        {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
        Enqueue for generation
      </Button>
      {!validation.valid && (
        <p className="text-xs text-amber-400">
          Choose at least one format for every selected candidate before enqueuing.
        </p>
      )}
    </div>
  );
}
