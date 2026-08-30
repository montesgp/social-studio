"use client";

/**
 * Discovery shortlist — single-selection instant analysis (CD-10, design D8/§8.2, WU-7).
 *
 * The chain proposes ranked candidates (unchanged scoring/classification from WU-4). The operator
 * selects EXACTLY ONE and it is analysed through the EXISTING single-analysis path:
 * `analyzeSingleContent` (POST `/api/social/single-analyzer/analyze`, i.e. `AnalyzeSingleContentCommand`
 * → `StartAsync` → `ISingleAnalysisJobQueue.EnqueueAsync`). This component authors NO backend code: no
 * new endpoint, no new queue, no new progress channel. On success it hands the returned analysis id to
 * `onAnalysisStarted`, whose caller drives the EXISTING polling overlay
 * (`single-analyzer-client.tsx`'s `POLL_INTERVAL_MS` / `ANALYSIS_STEPS` machinery) — reused verbatim,
 * never reimplemented here.
 */

import { useState } from "react";
import { Bot, CheckCircle2, Loader2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { analyzeSingleContent } from "@/lib/api/single-analyzer";
import {
  buildAnalyzeSelectedCandidatePayload,
  selectSingleCandidate,
  type DiscoveryShortlistCandidate,
} from "@/lib/social/content-discovery-shortlist";

export interface ContentDiscoveryShortlistCandidate extends DiscoveryShortlistCandidate {
  title: string;
  networkSource: string;
  thumbnailUrl?: string;
  views: number | null;
  likes: number | null;
  comments: number | null;
}

interface ContentDiscoveryShortlistProps {
  candidates: ContentDiscoveryShortlistCandidate[];
  clientId?: string;
  token?: string;
  /**
   * Called with the id (and originating clientId, if the analysis backend resolved one) once the
   * EXISTING single-analysis start call succeeds. The caller is responsible for polling status and
   * rendering the existing overlay — this component does not duplicate that machinery.
   */
  onAnalysisStarted: (analysisId: string) => void;
  onError?: (message: string) => void;
}

export function ContentDiscoveryShortlist({
  candidates,
  clientId,
  token,
  onAnalysisStarted,
  onError,
}: ContentDiscoveryShortlistProps) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isStarting, setIsStarting] = useState(false);

  function toggleSelect(candidateId: string) {
    setSelectedId((current) => selectSingleCandidate(current, candidateId));
  }

  async function analyzeSelected() {
    const payload = buildAnalyzeSelectedCandidatePayload(candidates, selectedId, clientId);
    if (!payload || isStarting) {
      return;
    }

    setIsStarting(true);
    try {
      // Exactly ONE POST to the EXISTING single-analysis start endpoint — no new backend transport.
      const started = await analyzeSingleContent(payload, token);
      onAnalysisStarted(started.id);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Could not start the analysis.";
      onError?.(message);
    } finally {
      setIsStarting(false);
    }
  }

  if (candidates.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground">
        No candidates found yet.
      </div>
    );
  }

  return (
    <div className="space-y-3" data-testid="content-discovery-shortlist">
      <div className="grid gap-3 md:grid-cols-2">
        {candidates.map((candidate) => {
          const isSelected = selectedId === candidate.id;
          return (
            <button
              type="button"
              key={candidate.id}
              onClick={() => toggleSelect(candidate.id)}
              aria-pressed={isSelected}
              className={`w-full rounded-2xl border p-4 text-left transition-colors ${
                isSelected ? "border-primary/70 bg-primary/10" : "border-border bg-background/40"
              }`}
            >
              <div className="flex items-start justify-between gap-2">
                <p className="font-semibold">{candidate.title}</p>
                {isSelected ? <CheckCircle2 className="h-4 w-4 shrink-0 text-primary" /> : null}
              </div>
              <p className="mt-1 text-xs text-muted-foreground">{candidate.networkSource}</p>
              <div className="mt-3 grid grid-cols-3 gap-2 text-xs text-muted-foreground">
                <span>Views: {candidate.views?.toLocaleString() ?? "—"}</span>
                <span>Likes: {candidate.likes?.toLocaleString() ?? "—"}</span>
                <span>Comments: {candidate.comments?.toLocaleString() ?? "—"}</span>
              </div>
            </button>
          );
        })}
      </div>

      <div className="flex items-center gap-2">
        <Button
          size="sm"
          disabled={!selectedId || isStarting}
          onClick={() => void analyzeSelected()}
        >
          {isStarting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Bot className="h-4 w-4" />}
          Analyze this one
        </Button>
        {selectedId ? <Badge variant="muted">1 candidate selected</Badge> : null}
      </div>
    </div>
  );
}
