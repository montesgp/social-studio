"use client";

// ProfileAnalyzerModule — "Mundo 2" lazy module boundary.
// recharts is imported ONLY inside this module so Mundo 1 bundle is unaffected (Design Decision 7).
// This file is the React.lazy target from page.tsx (see AnalyzerPageWithModeSwitch).

import { useState, useCallback, useEffect, useRef } from "react";
import { History, Trash2, RefreshCw, FolderOpen } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ProfileSourcePicker } from "@/components/social/profile-analyzer/ProfileSourcePicker";
import { AnalysisOverlay } from "@/components/social/analysis-overlay";
import { AnalyzerToolsToolbar } from "@/components/social/analyzer-tools-toolbar";
import { ProfileDashboard } from "@/components/social/profile-analyzer/dashboard/ProfileDashboard";
import { useAccessToken } from "@/components/auth/use-access-token";
import { openWhatsAppSupport } from "@/lib/support/support";
import {
  analyzeProfile,
  listProfileAnalyses,
  deleteProfileAnalysis,
  getProfileAnalysis,
  retryProfileAnalysis,
} from "@/lib/api/profile-analyzer";
import type {
  ProfileAnalysisStatusResponse,
  ProfileAnalysisHistoryItemDto,
  ProfileAnalysisStatus,
} from "@/lib/api/types/profile-analyzer";
import type { ProfileSourcePickerSubmitPayload } from "@/components/social/profile-analyzer/ProfileSourcePicker";

// ── Poll constants ────────────────────────────────────────────────────────────

const POLL_INTERVAL_MS = 2500;
const POLL_TIMEOUT_MS = 5 * 60 * 1000;
const TERMINAL_STATUSES: ProfileAnalysisStatus[] = ["Analyzed", "Failed"];

// ── Profile pipeline steps (shared component) ─────────────────────────────────

const PROFILE_STEPS = [
  { key: "queued", label: "Queued" },
  { key: "scraping", label: "Scraping profile" },
  { key: "scraped", label: "Profile scraped" },
  { key: "analyzing", label: "AI analysis" },
];

function profileStatusToPercent(status: ProfileAnalysisStatus | null): number {
  switch (status) {
    case "Requested": return 8;
    case "ScrapingProfile": return 30;
    case "ProfileScraped":
    case "ProfileScrapedPartial": return 60;
    case "Analyzing": return 82;
    case "Analyzed": return 100;
    case "Failed": return 100;
    default: return 0;
  }
}

function profileStatusToStepIndex(status: ProfileAnalysisStatus | null): number {
  if (!status) return 0;
  if (status === "Analyzed" || status === "Failed") return PROFILE_STEPS.length;
  const map: Record<string, number> = {
    Requested: 0,
    ScrapingProfile: 1,
    ProfileScraped: 2,
    ProfileScrapedPartial: 2,
    Analyzing: 3,
  };
  return map[status] ?? 0;
}

// ── Types ─────────────────────────────────────────────────────────────────────

type FlowState =
  | { stage: "pick" }
  | { stage: "polling"; analysisId: string }
  | { stage: "done"; response: ProfileAnalysisStatusResponse }
  | { stage: "viewing"; analysisId: string };

// ── Helpers ───────────────────────────────────────────────────────────────────

function statusLabel(status: ProfileAnalysisStatus): string {
  switch (status) {
    case "Requested": return "Queued";
    case "ScrapingProfile": return "Scraping";
    case "ProfileScraped": return "Profile scraped";
    case "ProfileScrapedPartial": return "Partial";
    case "Analyzing": return "Analyzing";
    case "Analyzed": return "Analyzed";
    case "Failed": return "Error";
    default: return status;
  }
}

function statusBadgeVariant(
  status: ProfileAnalysisStatus
): "success" | "destructive" | "warning" | "info" | "muted" {
  switch (status) {
    case "Analyzed": return "success";
    case "Failed": return "destructive";
    case "Analyzing": return "warning";
    case "ProfileScraped":
    case "ProfileScrapedPartial": return "info";
    default: return "muted";
  }
}

/** Show date AND time so same-day analyses are distinguishable (#2). */
function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString("en-US", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ProfileAnalyzerModule() {
  const token = useAccessToken() || undefined;
  const [flow, setFlow] = useState<FlowState>({ stage: "pick" });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // In-progress poll state (active while flow.stage === "polling")
  const [pollStatus, setPollStatus] = useState<ProfileAnalysisStatus | null>(null);
  const [pollIsPartial, setPollIsPartial] = useState(false);
  const [pollFailureCode, setPollFailureCode] = useState<string | null>(null);
  const [pollTimedOut, setPollTimedOut] = useState(false);
  const [isRetrying, setIsRetrying] = useState(false);
  const [retryError, setRetryError] = useState<string | null>(null);
  const pollCancelRef = useRef(false);
  const pollStartTimeRef = useRef(Date.now());

  // History
  const [historyItems, setHistoryItems] = useState<ProfileAnalysisHistoryItemDto[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [historyLoaded, setHistoryLoaded] = useState(false);
  const [openingId, setOpeningId] = useState<string | null>(null);

  // ── Polling effect ──────────────────────────────────────────────────────────

  useEffect(() => {
    if (flow.stage !== "polling") return;

    const analysisId = flow.analysisId;
    pollCancelRef.current = false;
    pollStartTimeRef.current = Date.now();
    setPollStatus(null);
    setPollIsPartial(false);
    setPollFailureCode(null);
    setPollTimedOut(false);

    const tick = async (): Promise<void> => {
      if (pollCancelRef.current) return;

      if (Date.now() - pollStartTimeRef.current > POLL_TIMEOUT_MS) {
        setPollTimedOut(true);
        return;
      }

      try {
        const resp = await getProfileAnalysis(analysisId, token);
        setPollStatus(resp.status as ProfileAnalysisStatus);
        setPollIsPartial(resp.status === "ProfileScrapedPartial");
        if (resp.failureCode) setPollFailureCode(resp.failureCode);

        if (TERMINAL_STATUSES.includes(resp.status as ProfileAnalysisStatus)) {
          setFlow({ stage: "done", response: resp });
          void loadHistory({ silent: true });
          return;
        }
      } catch {
        // Transient error — keep polling until timeout guard fires.
      }

      await new Promise<void>((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      void tick();
    };

    void tick();

    return () => {
      pollCancelRef.current = true;
    };
  }, [flow, token]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Submit handler ──────────────────────────────────────────────────────────

  const handleSourcePickerSubmit = useCallback(
    async (payload: ProfileSourcePickerSubmitPayload) => {
      setIsSubmitting(true);
      setSubmitError(null);
      try {
        const resp = await analyzeProfile(
          {
            clientId: payload.clientId,
            ownAccountId: payload.ownAccountId,
            profileUrl: payload.profileUrl,
          },
          token
        );
        setFlow({ stage: "polling", analysisId: resp.id });
      } catch (err: unknown) {
        const message =
          err && typeof err === "object" && "message" in err
            ? String((err as { message: unknown }).message)
            : "Could not start the analysis";
        setSubmitError(message);
      } finally {
        setIsSubmitting(false);
      }
    },
    [token]
  );

  // ── Retry AI analysis ───────────────────────────────────────────────────────

  const handleRetry = useCallback(
    async (id: string) => {
      if (isRetrying) return;
      setIsRetrying(true);
      setRetryError(null);
      try {
        await retryProfileAnalysis(id, token);
        // Re-enter polling stage so the useEffect re-runs with the same id.
        setFlow({ stage: "polling", analysisId: id });
      } catch (err: unknown) {
        const message =
          err && typeof err === "object" && "message" in err
            ? String((err as { message: unknown }).message)
            : "Could not retry the analysis";
        setRetryError(message);
      } finally {
        setIsRetrying(false);
      }
    },
    [isRetrying, token]
  );

  // ── Reset to picker ─────────────────────────────────────────────────────────

  const handleReset = useCallback(() => {
    setFlow({ stage: "pick" });
    setSubmitError(null);
    setRetryError(null);
  }, []);

  // ── Open from history (#1) ───────────────────────────────────────────────────

  const handleOpenFromHistory = useCallback(
    async (item: ProfileAnalysisHistoryItemDto) => {
      if (openingId) return;
      setOpeningId(item.id);
      try {
        // Mirror single-analyzer-client: fetch the full record to confirm it is Analyzed,
        // then enter "viewing" mode which renders ProfileDashboard (same as after fresh analysis).
        const resp = await getProfileAnalysis(item.id, token);
        if (
          resp.status === "Analyzed" ||
          (resp.status as string) === "Partial"
        ) {
          setFlow({ stage: "viewing", analysisId: item.id });
        }
      } catch {
        // Silently ignore — item stays in list, user can retry
      } finally {
        setOpeningId(null);
      }
    },
    [openingId, token]
  );

  // ── History ─────────────────────────────────────────────────────────────────

  const loadHistory = useCallback(
    async (opts?: { silent?: boolean }) => {
      if (!token) return;
      if (!opts?.silent) setHistoryLoading(true);
      try {
        const result = await listProfileAnalyses(1, token);
        setHistoryItems(result.items);
        setHistoryLoaded(true);
      } catch {
        // Keep previous list on transient error
      } finally {
        setHistoryLoading(false);
      }
    },
    [token]
  );

  // Load history on mount (once)
  const handleHistoryToggle = useCallback(() => {
    if (!historyLoaded) void loadHistory();
  }, [historyLoaded, loadHistory]);

  const handleDelete = useCallback(
    async (id: string) => {
      if (!token) return;
      setDeletingId(id);
      try {
        await deleteProfileAnalysis(id, token);
        setHistoryItems((prev) => prev.filter((i) => i.id !== id));
        // If user deletes the item they are currently viewing, go back to picker.
        if (flow.stage === "viewing" && flow.analysisId === id) {
          handleReset();
        }
      } catch {
        // Ignore; item stays in list
      } finally {
        setDeletingId(null);
      }
    },
    [token, flow, handleReset]
  );

  // ── Toolbar actions ─────────────────────────────────────────────────────────

  // "Save" for the profile analyzer: the analysis is always auto-persisted server-side.
  // There is no unsaved in-memory draft (unlike the content analyzer which has locally
  // drafted regeneration objections). We map Save to a silent history refresh so the
  // user gets immediate confirmation that the record is up-to-date in the list.
  const handleSave = useCallback(() => {
    void loadHistory({ silent: true });
  }, [loadHistory]);

  function handleHelp() {
    openWhatsAppSupport({
      feature: "profile-analyzer",
      details: {
        flowStage: flow.stage,
        analysisId:
          flow.stage === "polling"
            ? flow.analysisId
            : flow.stage === "done"
            ? flow.response.id
            : flow.stage === "viewing"
            ? flow.analysisId
            : null,
      },
    });
  }

  // The toolbar shows Save+New only when viewing a result (fresh analysis done OR opened from history).
  const isViewingResult =
    (flow.stage === "done" && flow.response.status === "Analyzed") ||
    flow.stage === "viewing";

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className="space-y-6">
      {/* Header with toolbar — mirrors single-analyzer-client header area */}
      <div className="flex items-center justify-between">
        <div />
        <AnalyzerToolsToolbar
          showResultActions={isViewingResult}
          onHelp={handleHelp}
          onSave={handleSave}
          onNew={handleReset}
        />
      </div>

      {/* Progress overlay — full-screen blocking, same as content analyzer (#3) */}
      <AnalysisOverlay
        visible={flow.stage === "polling"}
        steps={PROFILE_STEPS}
        currentStep={profileStatusToStepIndex(pollStatus)}
        percent={profileStatusToPercent(pollStatus)}
        isDone={pollStatus === "Analyzed"}
        isFailed={pollStatus === "Failed" || pollTimedOut}
        isPartial={pollIsPartial}
        runningTitle="Analyzing profile…"
        doneTitle="Analysis complete"
        failedTitle="Analysis failed"
        description="This usually takes 30–60 seconds. Keep this tab open."
        failureDetail={
          pollTimedOut
            ? "The analysis took too long. You can retry it from history."
            : pollFailureCode
            ? `Error code: ${pollFailureCode}`
            : null
        }
        ariaLabel="Analyzing profile"
      />

      {/* Source picker or progress (inline cancel) or done state */}
      {flow.stage === "pick" && (
        <Card className="border-border/40 bg-background/60">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm">Configure profile analysis</CardTitle>
          </CardHeader>
          <CardContent className="pt-0">
            <ProfileSourcePicker
              token={token}
              isSubmitting={isSubmitting}
              onSubmit={(payload) => void handleSourcePickerSubmit(payload)}
            />
            {submitError && (
              <p className="mt-2 text-xs text-destructive">{submitError}</p>
            )}
          </CardContent>
        </Card>
      )}

      {/* While polling: show a cancel button (overlay covers screen; this is the escape hatch) */}
      {flow.stage === "polling" && (
        <div className="pt-2">
          <Button variant="ghost" size="sm" onClick={handleReset} className="text-xs">
            Cancel and go back
          </Button>
        </div>
      )}

      {flow.stage === "done" && flow.response.status === "Analyzed" && (
        <ProfileDashboard
          analysisId={flow.response.id}
          token={token}
          onReset={handleReset}
        />
      )}

      {/* Viewing a dashboard opened from history (#1) */}
      {flow.stage === "viewing" && (
        <ProfileDashboard
          analysisId={flow.analysisId}
          token={token}
          onReset={handleReset}
        />
      )}

      {flow.stage === "done" && flow.response.status !== "Analyzed" && (
        <Card className="border-destructive/20 bg-destructive/5">
          <CardContent className="pt-4 pb-4 space-y-3">
            <p className="text-sm font-medium text-destructive">Analysis failed</p>
            {flow.response.failureCode && (
              <p className="text-xs text-muted-foreground">
                Code: <span className="font-mono">{flow.response.failureCode}</span>
              </p>
            )}
            {retryError && (
              <p className="text-xs text-destructive">{retryError}</p>
            )}
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                className="text-xs"
                disabled={isRetrying}
                onClick={() => void handleRetry(flow.response.id)}
              >
                {isRetrying ? (
                  <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                ) : (
                  <RefreshCw className="h-3.5 w-3.5" />
                )}
                Retry analysis
              </Button>
              <Button variant="ghost" size="sm" onClick={handleReset} className="text-xs">
                New analysis
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {/* History section */}
      <div className="space-y-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={handleHistoryToggle}
          className="h-auto px-0 py-0 text-xs text-muted-foreground/70 hover:bg-transparent hover:text-foreground"
        >
          <History className="h-3.5 w-3.5" />
          {historyLoaded ? "Analysis history" : "View history"}
        </Button>

        {historyLoaded && (
          <>
            {historyLoading && (
              <p className="text-xs text-muted-foreground/60">Loading history…</p>
            )}
            {!historyLoading && historyItems.length === 0 && (
              <p className="text-xs text-muted-foreground/60">No previous analyses.</p>
            )}
            {historyItems.length > 0 && (
              <Card className="border-border/40 bg-background/60">
                <CardContent className="py-2 divide-y divide-border/30">
                  {historyItems.map((item) => {
                    const itemStatus = item.status as ProfileAnalysisStatus;
                    const isViewable =
                      itemStatus === "Analyzed" ||
                      (itemStatus as string) === "Partial";
                    const isFailed = itemStatus === "Failed";

                    return (
                      <div
                        key={item.id}
                        className="flex items-center gap-3 py-2.5"
                      >
                        <div className="flex-1 min-w-0">
                          <p className="text-xs font-medium text-foreground/90 truncate">
                            {item.profileUrlOrHandle}
                          </p>
                          {/* #2: date + time so same-day analyses are distinguishable */}
                          <p className="text-xs text-muted-foreground/60">
                            {formatDateTime(item.createdAtUtc)}
                          </p>
                        </div>
                        <Badge
                          variant={statusBadgeVariant(itemStatus)}
                          className="text-xs shrink-0"
                        >
                          {statusLabel(itemStatus)}
                        </Badge>
                        {/* #1: open Analyzed/Partial items; Failed items show retry */}
                        {isViewable && (
                          <Button
                            size="sm"
                            variant="ghost"
                            className="h-7 px-2 text-muted-foreground hover:text-foreground"
                            disabled={openingId === item.id}
                            onClick={() => void handleOpenFromHistory(item)}
                            title="Open dashboard"
                          >
                            {openingId === item.id ? (
                              <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                            ) : (
                              <FolderOpen className="h-3.5 w-3.5" />
                            )}
                          </Button>
                        )}
                        {isFailed && (
                          <Button
                            size="sm"
                            variant="ghost"
                            className="h-7 px-2 text-muted-foreground hover:text-amber-500"
                            disabled={isRetrying}
                            onClick={() => void handleRetry(item.id)}
                            title="Retry analysis"
                          >
                            {isRetrying ? (
                              <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                            ) : (
                              <RefreshCw className="h-3.5 w-3.5" />
                            )}
                          </Button>
                        )}
                        <Button
                          size="sm"
                          variant="ghost"
                          className="h-7 px-2 text-muted-foreground hover:text-destructive"
                          disabled={deletingId === item.id}
                          onClick={() => void handleDelete(item.id)}
                          title="Delete analysis"
                        >
                          {deletingId === item.id ? (
                            <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                          ) : (
                            <Trash2 className="h-3.5 w-3.5" />
                          )}
                        </Button>
                      </div>
                    );
                  })}
                </CardContent>
              </Card>
            )}
          </>
        )}
      </div>
    </div>
  );
}
