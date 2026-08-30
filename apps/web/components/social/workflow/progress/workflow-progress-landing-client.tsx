"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { ArrowLeft, ArrowRight, Loader2, RefreshCw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { useDateFormatter } from "@/lib/format/use-date-formatter";
import { listContentWorkflowHistory } from "@/lib/api/content-workflow";
import type { ApiError, ContentWorkflowHistoryItem } from "@/lib/api/types";
import { historyStatusBadgeVariant } from "@/lib/social/workflow-progress-landing";

const LANDING_HISTORY_LIMIT = 50;

/**
 * Free-standing entry point into enqueued work: reachable at any time from the Generator header, NOT
 * only right after enqueueing (that one-shot link lives in `EnqueueSummary`). Lists every recent workflow
 * session for the active organization — same `listContentWorkflowHistory` read surface the Generator's
 * own history panel already uses — each row linking into its per-session progress view.
 */
export function WorkflowProgressLandingClient() {
  const token = useAccessToken();
  const { orgId, isInitialized: orgInitialized } = useActiveOrg();
  const { formatDateTime } = useDateFormatter();

  const [items, setItems] = useState<ContentWorkflowHistoryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!orgId || !token) return;

    setLoading(true);
    setError(null);
    try {
      const history = await listContentWorkflowHistory(orgId, LANDING_HISTORY_LIMIT, token);
      setItems(history);
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }, [orgId, token]);

  useEffect(() => {
    if (!orgInitialized) return;
    if (!orgId || !token) {
      setLoading(false);
      return;
    }
    void load();
  }, [orgInitialized, orgId, token, load]);

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
          <CardTitle className="text-xl">Enqueued work</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {error && <div className="rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-100">{error}</div>}

          {!error && loading && items.length === 0 && (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" /> Loading sessions…
            </div>
          )}

          {!error && !loading && items.length === 0 && (
            <p className="text-sm text-muted-foreground">
              No workflow sessions yet. Discover candidates from the Generator to get started.
            </p>
          )}

          {items.map((item) => (
            <Link
              key={item.workflowId}
              href={`/workflow/progress/${item.workflowId}`}
              className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-background/40 p-3 transition hover:border-primary/60"
            >
              <div>
                <p className="font-medium">{item.clientName}</p>
                <p className="text-xs text-muted-foreground">
                  {item.searchCriterion} • {item.strategyMode} • {item.networkSources.join(", ")}
                </p>
                <p className="mt-1 text-xs text-muted-foreground">
                  Updated: {formatDateTime(item.updatedAtUtc)} • Selected: {item.selectedCount}/{item.totalResults}
                </p>
              </div>
              <div className="flex items-center gap-2">
                <Badge variant={historyStatusBadgeVariant(item.status)}>{item.status}</Badge>
                <ArrowRight className="h-4 w-4 text-muted-foreground" />
              </div>
            </Link>
          ))}
        </CardContent>
      </Card>
    </div>
  );
}
