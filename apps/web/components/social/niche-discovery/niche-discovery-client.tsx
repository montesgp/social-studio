"use client";

/**
 * Niche Discovery screen (WU-14, design §10/§11). Mounts the viability dashboard that WU-12 built
 * but that no route reached: the operator starts a `DiscoveryRun` here, watches it move through
 * Requested → Running → Completed/PartiallyCompleted/Failed, and opens `ViabilityDashboardClient`
 * / `OpportunityDetail` once results exist.
 *
 * Thin on purpose: run-state interpretation lives in `lib/social/niche-discovery/run-state.ts`,
 * URL round-tripping in `run-url.ts`, and form validation/defaults in `start-run-form.ts` — all
 * unit-tested. This component only wires those modules to the API client and the sub-components
 * WU-12 already shipped.
 */

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Search } from "lucide-react";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { useAccessToken } from "@/components/auth/use-access-token";
import {
  getDiscoveryRun,
  listNicheDiscoveryMarkets,
  startDiscoveryRun,
  type DiscoveryRunStatusResponse,
  type MarketOption,
} from "@/lib/api/niche-discovery";
import { describeRunState, isRunPollable, isRunViewable, type RunStateTone } from "@/lib/social/niche-discovery/run-state";
import { formatMarketLabel } from "@/lib/social/niche-discovery/market-label";
import { buildRunPath } from "@/lib/social/niche-discovery/run-url";
import { describeRunProgress } from "@/lib/social/niche-discovery/run-progress";
import { show as showBlockingLoader, hide as hideBlockingLoader } from "@/lib/loading/blocking-loader-store";
import {
  CONTENT_FORMAT_OPTIONS,
  DEPTH_OPTIONS,
  MAX_WINDOW_DAYS,
  MIN_WINDOW_DAYS,
  NETWORK_KEY_OPTIONS,
  buildStartRunRequest,
  createDefaultStartRunFormState,
  validateStartRunForm,
  type StartRunFormState,
} from "@/lib/social/niche-discovery/start-run-form";
import { ViabilityDashboardClient } from "@/components/social/niche-discovery/viability-dashboard-client";
import { OpportunityDetail } from "@/components/social/niche-discovery/opportunity-detail";
import { RunHistoryList } from "@/components/social/niche-discovery/run-history-list";

const POLL_INTERVAL_MS = 2500;
const RUN_LOADER_ID = "social.niche-discovery.run";

const TONE_TO_BADGE_VARIANT: Record<RunStateTone, BadgeVariant> = {
  neutral: "muted",
  success: "success",
  warning: "warning",
  error: "destructive",
};

interface NicheDiscoveryClientProps {
  initialRunId: string | null;
}

export function NicheDiscoveryClient({ initialRunId }: NicheDiscoveryClientProps) {
  const router = useRouter();
  const token = useAccessToken();

  const [markets, setMarkets] = useState<MarketOption[]>([]);
  const [form, setForm] = useState<StartRunFormState>(() => createDefaultStartRunFormState());
  const [runId, setRunId] = useState<string | null>(initialRunId);
  const [run, setRun] = useState<DiscoveryRunStatusResponse | null>(null);
  const [selectedOpportunityId, setSelectedOpportunityId] = useState<string | null>(null);
  const [runStartedAt, setRunStartedAt] = useState<number | null>(null);
  const [elapsedMs, setElapsedMs] = useState(0);
  const [historyRefreshKey, setHistoryRefreshKey] = useState(0);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;
    listNicheDiscoveryMarkets(token)
      .then((response) => setMarkets(response.markets))
      .catch((err) => setError(err instanceof Error ? err.message : "Could not load the market catalog."));
  }, [token]);

  useEffect(() => {
    if (!runId || !token) return;

    let cancelled = false;
    let intervalId: number | undefined;

    function refresh() {
      getDiscoveryRun(runId!, token)
        .then((status) => {
          if (cancelled) return;
          setRun(status);
          // Stop polling once the run reaches a terminal state — nothing left to refresh.
          if (!isRunPollable(status.status) && intervalId !== undefined) {
            window.clearInterval(intervalId);
            intervalId = undefined;
            // A finished run belongs in the history immediately — it is the thing the operator comes
            // back for after closing the tool.
            setHistoryRefreshKey((key) => key + 1);
          }
        })
        .catch((err) => {
          if (!cancelled) setError(err instanceof Error ? err.message : "Could not load this run.");
        });
    }

    refresh();
    intervalId = window.setInterval(refresh, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      if (intervalId !== undefined) window.clearInterval(intervalId);
    };
  }, [runId, token]);

  // Elapsed time is what separates "slow" from "stopped" — the backend reports no heartbeat.
  useEffect(() => {
    if (runStartedAt === null) return;

    const tick = window.setInterval(() => setElapsedMs(Date.now() - runStartedAt), 1000);
    return () => window.clearInterval(tick);
  }, [runStartedAt]);

  // Blocks the screen while the run works: it is a background job with no visible surface, so without
  // this the operator cannot tell it apart from a page that did nothing.
  useEffect(() => {
    const progress = describeRunProgress({
      status: run?.status ?? (runId ? "Requested" : ""),
      elapsedMs,
      providerUnitsSpent: run?.providerUnitsSpent ?? 0,
    });

    if (!runId || !progress.isBlocking) {
      hideBlockingLoader(RUN_LOADER_ID);
      return;
    }

    return showBlockingLoader(RUN_LOADER_ID, {
      title: progress.title,
      // No granular sub-stages are available from the run status yet — a single stage whose
      // label tracks the real, live progress description (not a fabricated percentage).
      stages: [{ key: "run", label: progress.description || progress.title }],
    });
  }, [runId, run?.status, run?.providerUnitsSpent, elapsedMs]);

  // The loader must never outlive the screen that owns it.
  useEffect(() => () => hideBlockingLoader(RUN_LOADER_ID), []);

  const validation = useMemo(() => validateStartRunForm(form), [form]);
  const runState = run ? describeRunState(run.status, run.failureReasonKey) : null;

  async function handleStartRun() {
    if (!token || !validation.valid) return;

    setStarting(true);
    setError(null);
    setSelectedOpportunityId(null);
    setRun(null);

    try {
      const response = await startDiscoveryRun(buildStartRunRequest(form), token);
      setRunStartedAt(Date.now());
      setElapsedMs(0);
      setRunId(response.runId);
      setHistoryRefreshKey((key) => key + 1);
      router.replace(buildRunPath(response.runId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not start this discovery run.");
    } finally {
      setStarting(false);
    }
  }

  function openRun(historyRunId: string) {
    setRun(null);
    setSelectedOpportunityId(null);
    setError(null);
    setRunStartedAt(null);
    setElapsedMs(0);
    setRunId(historyRunId);
    router.replace(buildRunPath(historyRunId));
  }

  function startNewRun() {
    hideBlockingLoader(RUN_LOADER_ID);
    setRunStartedAt(null);
    setElapsedMs(0);
    setRunId(null);
    setRun(null);
    setSelectedOpportunityId(null);
    setError(null);
    router.replace(buildRunPath(null));
  }

  return (
    <div className="space-y-6">
      <section className="rounded-3xl border border-border bg-card p-6">
        <h1 className="text-3xl font-semibold tracking-tight md:text-5xl">Niche Discovery</h1>
        <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
          Start a discovery run for a source and target market to find which niche is worth building content for.
        </p>
      </section>

      {error ? (
        <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-4 text-sm">{error}</div>
      ) : null}

      {!runId || !run ? (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Start a discovery run</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 lg:grid-cols-3 xl:grid-cols-6">
            <Field label="Source market">
              <Select
                className="w-full"
                value={form.sourceMarketCode}
                onChange={(event) => setForm((f) => ({ ...f, sourceMarketCode: event.target.value }))}
              >
                <option value="">Select…</option>
                {markets.map((market) => (
                  <option key={market.code} value={market.code}>
                    {formatMarketLabel(market)}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Target market">
              <Select
                className="w-full"
                value={form.targetMarketCode}
                onChange={(event) => setForm((f) => ({ ...f, targetMarketCode: event.target.value }))}
              >
                <option value="">Select…</option>
                {markets.map((market) => (
                  <option key={market.code} value={market.code}>
                    {formatMarketLabel(market)}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Platform">
              <Select
                className="w-full"
                value={form.networkKey}
                onChange={(event) => setForm((f) => ({ ...f, networkKey: event.target.value }))}
              >
                {NETWORK_KEY_OPTIONS.map((key) => (
                  <option key={key} value={key}>
                    {key}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Content format">
              <Select
                className="w-full"
                value={form.contentFormat}
                onChange={(event) =>
                  setForm((f) => ({ ...f, contentFormat: event.target.value as StartRunFormState["contentFormat"] }))
                }
              >
                {CONTENT_FORMAT_OPTIONS.map((format) => (
                  <option key={format} value={format}>
                    {format}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label={`Analysis window (${MIN_WINDOW_DAYS}-${MAX_WINDOW_DAYS} days)`}>
              <input
                type="number"
                min={MIN_WINDOW_DAYS}
                max={MAX_WINDOW_DAYS}
                className="h-9 w-full rounded-md border border-border bg-background px-3 text-sm"
                value={form.windowDays}
                onChange={(event) => setForm((f) => ({ ...f, windowDays: Number(event.target.value) }))}
              />
            </Field>
            <Field label="Depth">
              <Select
                className="w-full"
                value={form.depth}
                onChange={(event) =>
                  setForm((f) => ({ ...f, depth: event.target.value as StartRunFormState["depth"] }))
                }
              >
                {DEPTH_OPTIONS.map((depth) => (
                  <option key={depth} value={depth}>
                    {depth}
                  </option>
                ))}
              </Select>
            </Field>
            <div className="lg:col-span-3 xl:col-span-6">
              <Field label="Seed terms (optional)">
                <textarea
                  rows={2}
                  className="w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
                  placeholder="Leave empty to discover what is already performing"
                  value={form.seedTermsText}
                  onChange={(event) => setForm((f) => ({ ...f, seedTermsText: event.target.value }))}
                />
                <p className="text-xs text-muted-foreground">
                  <strong>Leave this empty to discover niches</strong> — the run then sweeps what is
                  already performing and lets the niches emerge, without you naming one first.
                  Filling it in narrows the run to terms you already have in mind: they are searched
                  verbatim and never translated, so write them in the market&apos;s own language.
                </p>
              </Field>
            </div>
            <div className="lg:col-span-3 xl:col-span-6">
              <Button onClick={() => void handleStartRun()} disabled={starting || !validation.valid || !token}>
                {starting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
                Start discovery run
              </Button>
            </div>
          </CardContent>
        </Card>
      ) : null}

      {!runId || !run ? (
        <RunHistoryList token={token} refreshKey={historyRefreshKey} onOpenRun={openRun} />
      ) : null}

      {runId && run && runState ? (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              <span>Run status</span>
              <div className="flex items-center gap-2">
                <Badge variant={TONE_TO_BADGE_VARIANT[runState.tone]}>{runState.label}</Badge>
                <Button variant="outline" size="sm" onClick={startNewRun}>
                  Start new run
                </Button>
              </div>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {runState.status === "Failed" ? (
              <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-4 text-sm" data-testid="run-failed-reason">
                This run failed{runState.failureReasonKey ? `: ${runState.failureReasonKey}` : "."}
              </div>
            ) : null}
            {runState.isDegraded ? (
              <div
                className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 text-sm"
                data-testid="run-degraded-notice"
              >
                This run is partially completed — some sources did not finish, so the results below are an
                incomplete picture, not the full result.
              </div>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      {runId && run && isRunViewable(run.status) ? (
        <ViabilityDashboardClient runId={runId} token={token} onSelectOpportunity={setSelectedOpportunityId} />
      ) : null}

      {selectedOpportunityId && run ? (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              <span>Opportunity detail</span>
              <Button variant="outline" size="sm" onClick={() => setSelectedOpportunityId(null)}>
                Close
              </Button>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <OpportunityDetail
              opportunityId={selectedOpportunityId}
              runWindowDays={run.windowDays}
              runSemanticDegraded={run.semanticDegraded}
              runUnconfiguredSourceKeys={run.unconfiguredSourceKeys}
              runFailedSourceKeys={run.failedSourceKeys}
              token={token}
            />
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-2">
      <label className="text-sm font-medium">{label}</label>
      {children}
    </div>
  );
}
