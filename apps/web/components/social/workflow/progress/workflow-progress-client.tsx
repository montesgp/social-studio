"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { ArrowLeft, Loader2, RefreshCw } from "lucide-react";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { useDateFormatter } from "@/lib/format/use-date-formatter";
import { getContentWorkflowHistory } from "@/lib/api/content-workflow";
import type { ApiError, ContentWorkflowHistory } from "@/lib/api/types";
import { CANDIDATE_STATUS_CHECK_BACK_MESSAGE, describeCandidateStatus } from "@/lib/social/workflow-candidate-status";
import { summarizeWorkflowProgress, type WorkflowProgressBucket } from "@/lib/social/workflow-progress-summary";

const CANDIDATE_STATUS_BADGE_VARIANT: Record<string, BadgeVariant> = {
  pending: "info",
  success: "success",
  failed: "destructive"
};

const BUCKET_LABEL: Record<WorkflowProgressBucket, string> = {
  pending: "Pending",
  working: "Working",
  completed: "Completed",
  failed: "Failed"
};

const BUCKET_BADGE_VARIANT: Record<WorkflowProgressBucket, BadgeVariant> = {
  pending: "muted",
  working: "info",
  completed: "success",
  failed: "destructive"
};

export interface WorkflowProgressClientProps {
  workflowId: string;
}

/**
 * Simple, Generator-owned progress view for one workflow's enqueued candidates — the "View..." link
 * target after an operator enqueues candidates. Deliberately NOT the platform-admin
 * `/content-generation-jobs` screen (that one gates on `isPlatformAdmin`). No polling: an explicit
 * Refresh button re-fetches the same `GET /api/social/workflows/{id}/history` read surface the
 * Generator's own history panel already uses.
 */
export function WorkflowProgressClient({ workflowId }: WorkflowProgressClientProps) {
  const token = useAccessToken();
  const { orgId, isInitialized: orgInitialized } = useActiveOrg();
  const { formatDateTime } = useDateFormatter();

  const [history, setHistory] = useState<ContentWorkflowHistory | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!orgId || !token) return;

    setLoading(true);
    setError(null);
    try {
      const result = await getContentWorkflowHistory(orgId, workflowId, token);
      setHistory(result);
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }, [orgId, token, workflowId]);

  useEffect(() => {
    if (!orgInitialized) return;
    if (!orgId || !token) {
      setLoading(false);
      return;
    }
    void load();
  }, [orgInitialized, orgId, token, load]);

  const summary = history ? summarizeWorkflowProgress(history.candidates) : null;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Button asChild variant="ghost" size="sm">
          <Link href="/workflow">
            <ArrowLeft className="h-4 w-4" /> Back to Generator
          </Link>
        </Button>
        <Button size="sm" variant="outline" disabled={loading} onClick={() => void load()}>
          {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
          Refresh
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-xl">
            {history ? `${history.summary.clientName} — workflow progress` : "Workflow progress"}
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {error && <div className="rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-100">{error}</div>}

          {!error && history && (
            <>
              <p className="text-xs text-muted-foreground">
                {history.summary.searchCriterion} • {history.summary.strategyMode} •{" "}
                {history.summary.networkSources.join(", ")} • Updated {formatDateTime(history.summary.updatedAtUtc)}
              </p>

              {summary && (
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  {(Object.keys(BUCKET_LABEL) as WorkflowProgressBucket[]).map((bucket) => (
                    <div key={bucket} className="rounded-xl border border-border bg-background/40 p-3 text-center">
                      <p className="text-2xl font-semibold">{summary[bucket]}</p>
                      <Badge variant={BUCKET_BADGE_VARIANT[bucket]} className="mt-1">
                        {BUCKET_LABEL[bucket]}
                      </Badge>
                    </div>
                  ))}
                </div>
              )}

              {history.candidates.length === 0 ? (
                <p className="text-sm text-muted-foreground">No candidates have been enqueued for this workflow yet.</p>
              ) : (
                <div className="space-y-2">
                  {history.candidates.map((candidate) => {
                    const presentation = describeCandidateStatus(candidate.status);
                    return (
                      <div
                        key={candidate.candidateId}
                        className="rounded-lg border border-border/80 bg-background/70 px-3 py-2 text-sm"
                      >
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className="font-medium">{candidate.title || candidate.candidateId}</p>
                          <Badge variant={CANDIDATE_STATUS_BADGE_VARIANT[presentation.tone]}>{presentation.label}</Badge>
                        </div>
                        {candidate.failureCode && (
                          <p className="mt-1 text-xs text-muted-foreground">Reason: {candidate.failureCode}</p>
                        )}
                        {!presentation.isTerminal && (
                          <p className="mt-1 text-xs text-muted-foreground">{CANDIDATE_STATUS_CHECK_BACK_MESSAGE}</p>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </>
          )}

          {!error && !history && loading && (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" /> Loading progress…
            </div>
          )}

          {!error && !history && !loading && (
            <p className="text-sm text-muted-foreground">Sign in to an organization to view this workflow.</p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
