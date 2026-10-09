"use client";

import { useMemo, useState } from "react";
import { CheckCircle2, ChevronLeft, ChevronRight, LayoutList, Loader2, Play } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type { ContentWorkflowResult, PageResult, SocialNetwork } from "@/lib/api/types";
import { splitCandidatesByProcessedState } from "@/lib/social/workflow-candidate-grouping";
import { CandidateCard } from "./candidate-card";

export interface CandidateGridProps {
  resultsPage: PageResult<ContentWorkflowResult> | null;
  pageNumber: number;
  busy: boolean;
  viewMode: "grid" | "video";
  onViewModeChange: (mode: "grid" | "video") => void;
  activeResultId: string | null;
  selectedResultIds: string[];
  composedResultIds: Set<string>;
  availableNetworks: SocialNetwork[];
  onToggleSelected: (resultId: string) => void;
  onOpenResult: (resultId: string) => void;
  onSelectAll: () => void;
  onDeselectAll: () => void;
  onSaveSelection: () => void;
  onPreviousPage: () => void;
  onNextPage: () => void;
  /** Recover action for the Processed tab (see `splitCandidatesByProcessedState`). */
  onRecoverCandidate: (resultId: string) => void;
  recoveringCandidateId?: string | null;
}

/** Discovery results only — no bulk generation actions (removed per spec: `transcribe_and_build_script` /
 * `build_carousel` were fake bulk actions over the mocked pipeline). */
export function CandidateGrid({
  resultsPage,
  pageNumber,
  busy,
  viewMode,
  onViewModeChange,
  activeResultId,
  selectedResultIds,
  composedResultIds,
  availableNetworks,
  onToggleSelected,
  onOpenResult,
  onSelectAll,
  onDeselectAll,
  onSaveSelection,
  onPreviousPage,
  onNextPage,
  onRecoverCandidate,
  recoveringCandidateId
}: CandidateGridProps) {
  const [tab, setTab] = useState<"active" | "processed">("active");
  const results = useMemo(() => resultsPage?.items ?? [], [resultsPage]);
  const grouping = useMemo(() => splitCandidatesByProcessedState(results), [results]);
  const visibleResults = tab === "active" ? grouping.active : grouping.processed;

  return (
    <Card className="overflow-hidden">
      <CardHeader className="border-b border-border">
        <CardTitle className="flex items-center justify-between text-base">
          <span>Candidate results</span>
          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant={viewMode === "grid" ? "default" : "outline"}
              className="h-7 px-2"
              title="Grid view"
              onClick={() => onViewModeChange("grid")}
            >
              <LayoutList className="h-4 w-4" />
            </Button>
            <Button
              size="sm"
              variant={viewMode === "video" ? "default" : "outline"}
              className="h-7 px-2"
              title="Video view"
              onClick={() => onViewModeChange("video")}
            >
              <Play className="h-4 w-4" />
            </Button>
            <span className="text-xs font-normal text-muted-foreground">
              Page {resultsPage?.page ?? pageNumber} of {Math.max(resultsPage?.totalPages ?? 1, 1)}
            </span>
          </div>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4 p-4">
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            variant={tab === "active" ? "default" : "outline"}
            onClick={() => setTab("active")}
          >
            Active ({grouping.active.length})
          </Button>
          <Button
            size="sm"
            variant={tab === "processed" ? "default" : "outline"}
            onClick={() => setTab("processed")}
          >
            Processed ({grouping.processed.length})
          </Button>
        </div>

        {tab === "active" && (
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={onSelectAll}>
              Select all
            </Button>
            <Button variant="outline" size="sm" disabled={selectedResultIds.length === 0} onClick={onDeselectAll}>
              Deselect all
            </Button>
            <Button variant="secondary" size="sm" disabled={selectedResultIds.length === 0 || busy} onClick={onSaveSelection}>
              <CheckCircle2 className="h-4 w-4" /> Save selection ({selectedResultIds.length})
            </Button>
          </div>
        )}

        {results.length === 0 ? (
          <div className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground">
            Discover candidates to load paginated content candidates.
          </div>
        ) : visibleResults.length === 0 ? (
          <div className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground">
            {tab === "active"
              ? "No active candidates on this page — check the Processed tab."
              : "No processed candidates on this page yet."}
          </div>
        ) : (
          <div className={cn("gap-3", viewMode === "grid" ? "grid md:grid-cols-2" : "grid grid-cols-1")}>
            {visibleResults.map((result) => (
              <CandidateCard
                key={result.id}
                result={result}
                viewMode={viewMode}
                isActive={activeResultId === result.id}
                isSelected={selectedResultIds.includes(result.id)}
                isComposed={composedResultIds.has(result.id)}
                availableNetworks={availableNetworks}
                onToggleSelected={onToggleSelected}
                onOpen={onOpenResult}
                onRecover={tab === "processed" ? onRecoverCandidate : undefined}
                recovering={recoveringCandidateId === result.id}
              />
            ))}
          </div>
        )}

        <div className="flex items-center justify-between border-t border-border pt-4">
          <Button variant="outline" disabled={!resultsPage?.hasPreviousPage || busy} onClick={onPreviousPage}>
            <ChevronLeft className="h-4 w-4" /> Previous
          </Button>
          <span className="text-xs text-muted-foreground">
            {resultsPage?.totalCount ?? 0} results • {resultsPage?.pageSize ?? 12} per page
          </span>
          <Button variant="outline" disabled={!resultsPage?.hasNextPage || busy} onClick={onNextPage}>
            Next <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
        {busy && (
          <div className="flex items-center justify-center gap-2 text-xs text-muted-foreground">
            <Loader2 className="h-3.5 w-3.5 animate-spin" /> Working…
          </div>
        )}
      </CardContent>
    </Card>
  );
}
