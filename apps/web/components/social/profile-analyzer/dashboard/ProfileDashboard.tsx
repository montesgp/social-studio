"use client";

// ProfileDashboard — PA-16, PA-17, PA-18 top-level dashboard assembler.
// Fetches getProfileDashboard(id), composes all sub-components.
// Renders gracefully when optional sections are null or missing (PA-18).
// recharts imports are confined to the chart sub-components, all within
// the ProfileAnalyzerModule lazy boundary (Design Decision 7).

import { useCallback, useEffect, useState } from "react";
import { RefreshCw, AlertCircle } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { getProfileDashboard } from "@/lib/api/profile-analyzer";
import type { ProfileDashboardResponse } from "@/lib/api/types/profile-analyzer";
import { OverviewCards } from "@/components/social/profile-analyzer/dashboard/OverviewCards";
import { AvgViewsByWeekdayChart } from "@/components/social/profile-analyzer/dashboard/AvgViewsByWeekdayChart";
import { ContentTypeMixChart } from "@/components/social/profile-analyzer/dashboard/ContentTypeMixChart";
import { EngagementTrendChart } from "@/components/social/profile-analyzer/dashboard/EngagementTrendChart";
import { AiNarrativePanel } from "@/components/social/profile-analyzer/dashboard/AiNarrativePanel";
import { ComparisonVsObjectivesPanel } from "@/components/social/profile-analyzer/dashboard/ComparisonVsObjectivesPanel";
import { ImputePanel } from "@/components/social/profile-analyzer/impute/ImputePanel";

// ── Types ─────────────────────────────────────────────────────────────────────

interface ProfileDashboardProps {
  analysisId: string;
  token: string | undefined;
  /** Called when user requests a new analysis from this view. */
  onReset: () => void;
  isPdfCaptureMode?: boolean;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ProfileDashboard({
  analysisId,
  token,
  onReset,
  isPdfCaptureMode = false,
}: ProfileDashboardProps) {
  const [dashboard, setDashboard] = useState<ProfileDashboardResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchDashboard = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await getProfileDashboard(analysisId, token);
      setDashboard(data);
    } catch (err: unknown) {
      const message =
        err && typeof err === "object" && "message" in err
          ? String((err as { message: unknown }).message)
          : "Could not load the dashboard";
      setError(message);
    } finally {
      setLoading(false);
    }
  }, [analysisId, token]);

  useEffect(() => {
    void fetchDashboard();
  }, [fetchDashboard]);

  // ── Loading state ──────────────────────────────────────────────────────────

  if (loading) {
    return (
      <div className="flex items-center justify-center gap-2 py-12 text-muted-foreground/60">
        <RefreshCw className="h-4 w-4 animate-spin" />
        <span className="text-sm">Loading dashboard…</span>
      </div>
    );
  }

  // ── Error state ────────────────────────────────────────────────────────────

  if (error || !dashboard) {
    return (
      <Card className="border-destructive/30 bg-destructive/5">
        <CardContent className="pt-4 pb-4 space-y-3">
          <div className="flex items-center gap-2 text-destructive">
            <AlertCircle className="h-4 w-4" />
            <p className="text-sm font-medium">Could not load the dashboard</p>
          </div>
          {error && <p className="text-xs text-muted-foreground">{error}</p>}
          <div className="flex gap-2">
            <Button variant="outline" size="sm" className="text-xs" onClick={() => void fetchDashboard()}>
              Retry
            </Button>
            <Button variant="ghost" size="sm" className="text-xs" onClick={onReset}>
              New analysis
            </Button>
          </div>
        </CardContent>
      </Card>
    );
  }

  // ── Dashboard ──────────────────────────────────────────────────────────────

  const hasChartData = dashboard.stats !== null;
  const hasAiAnalysis = dashboard.aiAnalysis !== null;
  const hasComparison = dashboard.clientId !== null;

  return (
    <div className="space-y-6" data-profile-dashboard-ready="true">
      <div className="space-y-6" data-pdf-section="profile-summary">
        {/* Profile header */}
        <div className="flex items-start gap-3">
          <div>
            <h2 className="text-base font-semibold text-foreground">
              Profile analysis
            </h2>
            <p className="text-xs text-muted-foreground/70 font-mono mt-0.5">
              {dashboard.profileUrlOrHandle}
            </p>
          </div>
        </div>

        {/* Overview metrics cards */}
        <OverviewCards dashboard={dashboard} />
      </div>

      {/* Charts section — only when stats are available */}
      {hasChartData && (
        <Card
          className="border-border/40 bg-background/60"
          data-pdf-section="profile-charts"
        >
          <CardContent className="pt-4 pb-4 space-y-5">
            <div className="grid gap-5 lg:grid-cols-2">
              <AvgViewsByWeekdayChart
                stats={dashboard.stats}
                captureMode={isPdfCaptureMode}
              />
              <ContentTypeMixChart
                stats={dashboard.stats}
                captureMode={isPdfCaptureMode}
              />
            </div>
            <EngagementTrendChart
              stats={dashboard.stats}
              captureMode={isPdfCaptureMode}
            />
          </CardContent>
        </Card>
      )}

      {/* AI narrative */}
      {hasAiAnalysis && (
        <Card
          className="border-border/40 bg-background/60"
          data-pdf-section="profile-ai-analysis"
        >
          <CardContent className="pt-4 pb-4">
            <AiNarrativePanel aiAnalysis={dashboard.aiAnalysis} />
          </CardContent>
        </Card>
      )}

      {/* Comparison vs objectives — only for client-linked analyses */}
      {hasComparison && (
        <Card
          className="border-border/40 bg-background/60"
          data-pdf-section="profile-comparison"
        >
          <CardContent className="pt-4 pb-4">
            <ComparisonVsObjectivesPanel
              clientId={dashboard.clientId}
              comparison={dashboard.comparison}
              aiAnalysis={dashboard.aiAnalysis}
            />
          </CardContent>
        </Card>
      )}

      {/* Impute panel — PA-20/21/22: only when Analyzed (guaranteed by caller) + clientId non-null */}
      <div data-pdf-exclude="true">
        {dashboard.clientId !== null && dashboard.aiAnalysis !== null ? (
          <ImputePanel
            analysisId={analysisId}
            clientId={dashboard.clientId}
            aiAnalysis={dashboard.aiAnalysis}
            token={token}
          />
        ) : (
          <div className="flex items-center justify-center h-10 rounded-md border border-border/20 border-dashed">
            <p className="text-xs text-muted-foreground/50">
              Client field updates are available only for analyses linked to a system client
            </p>
          </div>
        )}
      </div>

      {/* Graceful empty state: no data at all (PA-18) */}
      {!hasChartData && !hasAiAnalysis && (
        <div
          className="flex items-center justify-center h-20 rounded-md border border-border/30 border-dashed"
          data-pdf-section="profile-empty-state"
        >
          <p className="text-xs text-muted-foreground/60">
            No data available for this analysis
          </p>
        </div>
      )}
    </div>
  );
}
