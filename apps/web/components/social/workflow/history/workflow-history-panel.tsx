"use client";

import { ChevronDown, ChevronUp, Clock3, History, Loader2 } from "lucide-react";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import type { ContentWorkflowHistory, ContentWorkflowHistoryItem, SocialClient } from "@/lib/api/types";
import { CANDIDATE_STATUS_CHECK_BACK_MESSAGE, describeCandidateStatus } from "@/lib/social/workflow-candidate-status";

const CANDIDATE_STATUS_BADGE_VARIANT: Record<string, BadgeVariant> = {
  pending: "info",
  success: "success",
  failed: "destructive"
};

export interface WorkflowHistoryPanelProps {
  historyItems: ContentWorkflowHistoryItem[];
  clients: SocialClient[];
  historyClientFilter: string;
  onHistoryClientFilterChange: (clientId: string) => void;
  activeWorkflowId: string | null;
  expandedHistoryId: string | null;
  historyDetailMap: Record<string, ContentWorkflowHistory>;
  formatDate: (value: string) => string;
  onResume: (item: ContentWorkflowHistoryItem) => void;
  onToggleExpand: (item: ContentWorkflowHistoryItem) => void;
}

/** Client-filtered workflow history (spec: "Optional client-scoped history filter"). */
export function WorkflowHistoryPanel({
  historyItems,
  clients,
  historyClientFilter,
  onHistoryClientFilterChange,
  activeWorkflowId,
  expandedHistoryId,
  historyDetailMap,
  formatDate,
  onResume,
  onToggleExpand
}: WorkflowHistoryPanelProps) {
  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between gap-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <History className="h-4 w-4 text-primary" /> Workflow history
        </CardTitle>
        <Select
          className="min-w-[180px]"
          value={historyClientFilter}
          onChange={(event) => onHistoryClientFilterChange(event.target.value)}
        >
          <option value="">All clients</option>
          {clients.map((client) => (
            <option key={client.id} value={client.id}>
              {client.name}
            </option>
          ))}
        </Select>
      </CardHeader>
      <CardContent className="space-y-3">
        {historyItems.length === 0 ? (
          <p className="text-sm text-muted-foreground">No history yet for this filter.</p>
        ) : (
          historyItems.map((item) => {
            const isExpanded = expandedHistoryId === item.workflowId;
            const detail = historyDetailMap[item.workflowId];

            return (
              <div key={item.workflowId} className="rounded-xl border border-border bg-background/40">
                <div className="flex flex-wrap items-start justify-between gap-3 p-3">
                  <div>
                    <p className="font-medium">{item.clientName}</p>
                    <p className="text-xs text-muted-foreground">
                      {item.searchCriterion} • {item.strategyMode} • {item.networkSources.join(", ")}
                    </p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      Updated: {formatDate(item.updatedAtUtc)} • Selected: {item.selectedCount}/{item.totalResults}
                    </p>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant={item.status === "completed" ? "success" : "muted"}>{item.status}</Badge>
                    <Button size="sm" variant={activeWorkflowId === item.workflowId ? "default" : "outline"} onClick={() => onResume(item)}>
                      <Clock3 className="h-4 w-4" />
                      {activeWorkflowId === item.workflowId ? "Active" : "Resume"}
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      className="h-8 px-2"
                      title={isExpanded ? "Collapse summary" : "Expand summary"}
                      onClick={() => onToggleExpand(item)}
                    >
                      {isExpanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                    </Button>
                  </div>
                </div>

                {isExpanded && (
                  <div className="border-t border-border/60 px-3 pb-3 pt-2">
                    {detail ? (
                      detail.candidates.length === 0 ? (
                        <p className="text-xs text-muted-foreground">
                          No candidates have been composed and enqueued for this workflow yet.
                        </p>
                      ) : (
                        <div className="space-y-2">
                          {detail.candidates.map((candidate) => {
                            const presentation = describeCandidateStatus(candidate.status);
                            return (
                              <div
                                key={candidate.candidateId}
                                className="rounded-lg border border-border/80 bg-background/70 px-3 py-2 text-xs"
                              >
                                <div className="flex flex-wrap items-center justify-between gap-2">
                                  <p className="font-medium">{candidate.title || candidate.candidateId}</p>
                                  <Badge variant={CANDIDATE_STATUS_BADGE_VARIANT[presentation.tone]}>
                                    {presentation.label}
                                  </Badge>
                                </div>
                                {candidate.failureCode && (
                                  <p className="mt-1 text-muted-foreground">Reason: {candidate.failureCode}</p>
                                )}
                                {!presentation.isTerminal && (
                                  <p className="mt-1 text-muted-foreground">{CANDIDATE_STATUS_CHECK_BACK_MESSAGE}</p>
                                )}
                              </div>
                            );
                          })}
                        </div>
                      )
                    ) : (
                      <div className="flex items-center gap-2 text-xs text-muted-foreground">
                        <Loader2 className="h-3.5 w-3.5 animate-spin" /> Loading summary…
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })
        )}
      </CardContent>
    </Card>
  );
}
