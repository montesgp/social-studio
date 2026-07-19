"use client";

import { useEffect, useState } from "react";
import {
  AlertOctagon,
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock3,
  Copy,
  History,
  ImageIcon,
  Layers,
  Loader2,
  RefreshCcw,
  RotateCw,
  XCircle
} from "lucide-react";
import {
  getContentGenerationJobTimeline,
  listContentGenerationJobs
} from "@/lib/api/content-generation-audit";
import {
  ApiError,
  ContentGenerationJobListItem,
  ContentGenerationJobListResponse,
  ContentGenerationJobTimeline,
  ContentGenerationTimelineFormatOutput,
  ContentGenerationTimelineStep
} from "@/lib/api/types";
import {
  classifySlideOutcome,
  describeStepType,
  formatBytes,
  formatCostUsd,
  formatDurationSeconds,
  formatElapsedMs,
  formatTokenUsage,
  getJobStatusTone,
  hadRetry,
  isFailedStatus,
  isPartialStatus
} from "@/lib/content-generation-audit-format";
import { useAccessToken } from "@/components/auth/use-access-token";
import { Badge, BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { cn } from "@/lib/utils";

const PAGE_SIZE = 20;

export function ContentGenerationJobsClient() {
  const token = useAccessToken();
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);

  if (selectedJobId) {
    return <JobTimelineView jobId={selectedJobId} token={token} onBack={() => setSelectedJobId(null)} />;
  }

  return <JobIndexView token={token} onSelectJob={setSelectedJobId} />;
}

// ── Job index ──────────────────────────────────────────────────────────────────

function JobIndexView({
  token,
  onSelectJob
}: {
  token: string;
  onSelectJob: (jobId: string) => void;
}) {
  const [page, setPage] = useState<ContentGenerationJobListResponse | null>(null);
  const [pageNumber, setPageNumber] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) {
      setLoading(false);
      return;
    }
    void load(pageNumber);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, pageNumber]);

  async function load(nextPage: number) {
    setLoading(true);
    setError(null);
    try {
      const data = await listContentGenerationJobs({ page: nextPage, pageSize: PAGE_SIZE }, token || undefined);
      setPage(data);
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }

  const items = page?.items ?? [];
  const totalCount = page?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <div className="space-y-6">
      <section className="rounded-3xl border border-border bg-card p-6 shadow-2xl">
        <Badge variant="info" className="w-fit">
          <History className="mr-1 h-3 w-3" /> Content generation audit
        </Badge>
        <h1 className="mt-3 text-3xl font-semibold tracking-tight md:text-5xl">Content generation jobs</h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground md:text-base">
          Every content-generation job across all organizations, most recent first. Open a job to see its full
          step-by-step timeline — no log file required.
        </p>
      </section>

      {error ? (
        <div className="rounded-xl border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      ) : null}

      <Card className="overflow-hidden">
        <CardHeader className="border-b border-border">
          <CardTitle className="flex flex-wrap items-center justify-between gap-3 text-base">
            <span>Jobs</span>
            <div className="flex items-center gap-2">
              <Button variant="outline" size="sm" className="h-7 px-2" onClick={() => void load(pageNumber)}>
                <RefreshCcw className="h-4 w-4" />
              </Button>
              <span className="text-xs font-normal text-muted-foreground">
                Page {page?.page ?? pageNumber} of {totalPages}
              </span>
            </div>
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {loading ? (
            <div className="p-4">
              {Array.from({ length: 6 }).map((_, index) => (
                <div key={index} className="mb-3 h-12 animate-pulse rounded-md border border-border bg-muted/40" />
              ))}
            </div>
          ) : items.length === 0 ? (
            <EmptyState />
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Status</TableHead>
                    <TableHead>Created</TableHead>
                    <TableHead>Duration</TableHead>
                    <TableHead>Formats</TableHead>
                    <TableHead>Assets</TableHead>
                    <TableHead>Error</TableHead>
                    <TableHead>Job / analysis</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((item) => (
                    <JobRow key={item.jobId} item={item} onOpen={() => onSelectJob(item.jobId)} />
                  ))}
                </TableBody>
              </Table>
            </div>
          )}

          <div className="flex items-center justify-between border-t border-border p-4">
            <Button
              variant="outline"
              disabled={pageNumber <= 1 || loading}
              onClick={() => setPageNumber((value) => Math.max(1, value - 1))}
            >
              <ChevronLeft className="h-4 w-4" /> Previous
            </Button>
            <span className="text-xs text-muted-foreground">
              {totalCount} job{totalCount === 1 ? "" : "s"} • {PAGE_SIZE} per page
            </span>
            <Button
              variant="outline"
              disabled={pageNumber >= totalPages || loading}
              onClick={() => setPageNumber((value) => value + 1)}
            >
              Next <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function JobRow({ item, onOpen }: { item: ContentGenerationJobListItem; onOpen: () => void }) {
  const incomplete = item.persistedFormatCount < item.requestedFormatCount;

  return (
    <TableRow className="cursor-pointer" onClick={onOpen}>
      <TableCell>
        <StatusBadge status={item.status} />
      </TableCell>
      <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
        {formatDateTime(item.createdAt)}
      </TableCell>
      <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
        {formatJobDuration(item.startedAt, item.completedAt)}
      </TableCell>
      <TableCell className="text-xs">
        <span className={cn(incomplete && "font-semibold text-amber-700 dark:text-amber-300")}>
          {item.persistedFormatCount}/{item.requestedFormatCount}
        </span>
      </TableCell>
      <TableCell className="text-xs text-muted-foreground">{item.assetCount}</TableCell>
      <TableCell className="text-xs">
        {item.errorCode ? (
          <Badge variant="destructive" className="font-mono text-[0.65rem]">
            {item.errorCode}
          </Badge>
        ) : (
          <span className="text-muted-foreground">—</span>
        )}
      </TableCell>
      <TableCell className="text-xs">
        <IdValue label="Job" value={item.jobId} />
        <IdValue label="Analysis" value={item.analysisId} />
      </TableCell>
    </TableRow>
  );
}

function EmptyState() {
  return (
    <div className="flex min-h-72 flex-col items-center justify-center p-8 text-center">
      <div className="mb-4 rounded-3xl border border-border bg-muted p-5">
        <History className="h-8 w-8 text-primary" />
      </div>
      <h2 className="text-lg font-semibold">No content generation jobs yet</h2>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">
        Jobs will appear here as soon as the first content package is generated.
      </p>
    </div>
  );
}

// ── Job timeline detail ────────────────────────────────────────────────────────

function JobTimelineView({
  jobId,
  token,
  onBack
}: {
  jobId: string;
  token: string;
  onBack: () => void;
}) {
  const [timeline, setTimeline] = useState<ContentGenerationJobTimeline | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) {
      setLoading(false);
      return;
    }
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, jobId]);

  async function load() {
    setLoading(true);
    setError(null);
    setNotFound(false);
    try {
      const data = await getContentGenerationJobTimeline(jobId, token || undefined);
      if (data === null) {
        setNotFound(true);
        setTimeline(null);
      } else {
        setTimeline(data);
      }
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="space-y-6">
      <Button variant="outline" size="sm" onClick={onBack}>
        <ArrowLeft className="h-4 w-4" /> Back to jobs
      </Button>

      {loading ? (
        <Card>
          <CardContent className="flex items-center gap-2 p-6 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading job timeline...
          </CardContent>
        </Card>
      ) : error ? (
        <div className="rounded-xl border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      ) : notFound ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center gap-2 p-10 text-center">
            <AlertTriangle className="h-8 w-8 text-muted-foreground" />
            <h2 className="text-lg font-semibold">Job not found</h2>
            <p className="max-w-md text-sm text-muted-foreground">
              No content generation job exists with id <span className="font-mono">{jobId}</span>. It may have been
              deleted, or the id was mistyped.
            </p>
          </CardContent>
        </Card>
      ) : timeline ? (
        <JobTimelineDetail timeline={timeline} />
      ) : null}
    </div>
  );
}

function JobTimelineDetail({ timeline }: { timeline: ContentGenerationJobTimeline }) {
  return (
    <div className="space-y-6">
      <JobHeader timeline={timeline} />

      {timeline.endedWithoutPersistingEverything ? (
        <div className="flex items-start gap-3 rounded-xl border border-amber-500/50 bg-amber-500/10 px-4 py-3 text-sm text-amber-900 dark:text-amber-200">
          <AlertOctagon className="mt-0.5 h-5 w-5 shrink-0" />
          <div>
            <p className="font-semibold">Ended without persisting everything</p>
            <p className="mt-1 text-xs opacity-90">
              This job produced output for at least one format but did not finish persisting every requested
              format. This is different from producing nothing — check the steps below for where it stopped.
            </p>
          </div>
        </div>
      ) : null}

      <Card>
        <CardHeader className="border-b border-border">
          <CardTitle className="flex items-center gap-2 text-base">
            <Layers className="h-4 w-4 text-primary" /> Steps
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {timeline.steps.length === 0 ? (
            <div className="p-8 text-center text-sm text-muted-foreground">
              No execution steps were recorded for this job.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Time</TableHead>
                    <TableHead>Step</TableHead>
                    <TableHead>Provider</TableHead>
                    <TableHead>Duration</TableHead>
                    <TableHead>Outcome</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {timeline.steps.map((step, index) => (
                    <StepRow key={`${step.occurredAtUtc}-${index}`} step={step} />
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="border-b border-border">
          <CardTitle className="flex items-center gap-2 text-base">
            <ImageIcon className="h-4 w-4 text-primary" /> Produced formats
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3 p-4">
          {timeline.formats.length === 0 ? (
            <div className="p-6 text-center text-sm text-muted-foreground">
              No formats were persisted for this job.
            </div>
          ) : (
            timeline.formats.map((format) => <FormatOutputCard key={format.formatResultId} format={format} />)
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function JobHeader({ timeline }: { timeline: ContentGenerationJobTimeline }) {
  return (
    <Card>
      <CardContent className="space-y-4 p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <StatusBadge status={timeline.status} />
              {timeline.errorCode ? (
                <Badge variant="destructive" className="font-mono text-[0.65rem]">
                  {timeline.errorCode}
                </Badge>
              ) : null}
            </div>
            <h1 className="text-2xl font-semibold tracking-tight">Job timeline</h1>
            {timeline.statusReason ? (
              <p className="max-w-2xl text-sm text-muted-foreground">{timeline.statusReason}</p>
            ) : null}
          </div>
          <div className="rounded-xl border border-border bg-muted/30 px-4 py-3 text-right">
            <p className="text-[0.65rem] uppercase tracking-widest text-muted-foreground">Total duration</p>
            <p className="mt-1 flex items-center justify-end gap-1 text-xl font-semibold">
              <Clock3 className="h-4 w-4 text-muted-foreground" />
              {formatDurationSeconds(timeline.totalDurationSeconds)}
            </p>
          </div>
        </div>

        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <IdValue label="Job id" value={timeline.jobId} block />
          <IdValue label="Organization" value={timeline.organizationId} block />
          <IdValue label="Analysis" value={timeline.analysisId} block />
          <IdValue label="Client" value={timeline.clientId ?? "—"} block />
        </div>

        <div>
          <p className="text-[0.65rem] uppercase tracking-widest text-muted-foreground">Requested formats</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {timeline.requestedFormats.map((format) => (
              <Badge key={format} variant="outline">
                {format}
              </Badge>
            ))}
          </div>
        </div>

        <div className="grid gap-3 text-xs text-muted-foreground md:grid-cols-3">
          <span>Created: {formatDateTime(timeline.createdAt)}</span>
          <span>Started: {timeline.startedAt ? formatDateTime(timeline.startedAt) : "—"}</span>
          <span>Completed: {timeline.completedAt ? formatDateTime(timeline.completedAt) : "—"}</span>
        </div>
      </CardContent>
    </Card>
  );
}

function StepRow({ step }: { step: ContentGenerationTimelineStep }) {
  const retried = hadRetry(step);

  return (
    <TableRow>
      <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
        {formatTime(step.occurredAtUtc)}
      </TableCell>
      <TableCell className="text-xs">
        <div className="flex flex-col gap-1">
          <span className="font-medium">{describeStepType(step.stepType)}</span>
          {step.formatKey ? <span className="text-muted-foreground">{step.formatKey}</span> : null}
          {step.stepType === "slide-outcome" && step.slideIndex !== null ? (
            <span className="text-muted-foreground">Slide #{step.slideIndex}</span>
          ) : null}
        </div>
      </TableCell>
      <TableCell className="text-xs text-muted-foreground">
        <div className="flex flex-col gap-1">
          <span>{step.providerKey}</span>
          <span className="text-[0.65rem] uppercase tracking-wide opacity-70">{step.capabilityKey}</span>
          {step.stepType === "usage-event" ? <UsageEventDetails step={step} /> : null}
        </div>
      </TableCell>
      <TableCell className="whitespace-nowrap text-xs">{formatElapsedMs(step.elapsedMs)}</TableCell>
      <TableCell className="text-xs">
        <StepOutcome step={step} retried={retried} />
      </TableCell>
    </TableRow>
  );
}

/**
 * Model + token/cost detail shown under the provider column for "usage-event" steps (Change 4) —
 * kept compact since the operator's primary concerns are what ran, how long, and whether it failed;
 * cost/tokens are secondary context, not the headline of the row.
 */
function UsageEventDetails({ step }: { step: ContentGenerationTimelineStep }) {
  const tokenUsage = formatTokenUsage(step.inputTokens, step.outputTokens);
  const cost = formatCostUsd(step.estimatedCostUsd);

  if (!step.model && !tokenUsage && !cost) {
    return null;
  }

  return (
    <span className="text-[0.65rem] normal-case tracking-normal opacity-90">
      {[step.model, tokenUsage, cost].filter(Boolean).join(" • ")}
    </span>
  );
}

function StepOutcome({ step, retried }: { step: ContentGenerationTimelineStep; retried: boolean }) {
  if (step.stepType === "slide-outcome") {
    const outcomeClass = classifySlideOutcome(step.slideOutcome);
    const variant: BadgeVariant =
      outcomeClass === "succeeded" ? "success" : outcomeClass === "degraded" ? "warning" : outcomeClass === "failed" ? "destructive" : "muted";
    return (
      <div className="flex flex-col gap-1">
        <Badge variant={variant} className="w-fit">
          {outcomeClass === "degraded" ? (
            <AlertTriangle className="mr-1 h-3 w-3" />
          ) : outcomeClass === "succeeded" ? (
            <CheckCircle2 className="mr-1 h-3 w-3" />
          ) : outcomeClass === "failed" ? (
            <XCircle className="mr-1 h-3 w-3" />
          ) : null}
          {step.slideOutcome ?? "Unknown"}
        </Badge>
        {step.slideOutcomeReason ? (
          <span className="text-[0.7rem] text-muted-foreground">{step.slideOutcomeReason}</span>
        ) : null}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      <Badge variant={step.succeeded ? "success" : "destructive"} className="w-fit">
        {step.succeeded ? <CheckCircle2 className="mr-1 h-3 w-3" /> : <XCircle className="mr-1 h-3 w-3" />}
        {step.succeeded ? "Succeeded" : "Failed"}
      </Badge>
      {retried ? (
        <span className="flex items-center gap-1 text-[0.7rem] text-amber-700 dark:text-amber-300">
          <RotateCw className="h-3 w-3" /> Attempt {step.attemptNumber} • {step.retryCount} retr
          {step.retryCount === 1 ? "y" : "ies"}
        </span>
      ) : null}
      {step.failureCode ? (
        <span className="font-mono text-[0.7rem] text-destructive">{step.failureCode}</span>
      ) : null}
    </div>
  );
}

function FormatOutputCard({ format }: { format: ContentGenerationTimelineFormatOutput }) {
  const tone = getJobStatusTone(format.status);

  return (
    <div className="rounded-xl border border-border bg-background/40 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="font-medium">{format.formatKey}</span>
          <Badge variant={tone}>{format.status}</Badge>
          {format.errorCode ? (
            <Badge variant="destructive" className="font-mono text-[0.65rem]">
              {format.errorCode}
            </Badge>
          ) : null}
        </div>
        <span className="text-xs text-muted-foreground">Updated {formatDateTime(format.updatedAt)}</span>
      </div>

      {format.assets.length === 0 ? (
        <p className="mt-3 text-xs text-muted-foreground">No assets persisted for this format.</p>
      ) : (
        <div className="mt-3 overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>#</TableHead>
                <TableHead>Filename</TableHead>
                <TableHead>Type</TableHead>
                <TableHead>Size</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {format.assets.map((asset) => (
                <TableRow key={asset.assetId}>
                  <TableCell className="text-xs text-muted-foreground">{asset.assetIndex}</TableCell>
                  <TableCell className="max-w-[16rem] truncate text-xs" title={asset.filename}>
                    {asset.filename}
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">{asset.mimeType}</TableCell>
                  <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
                    {formatBytes(asset.sizeBytes)}
                  </TableCell>
                  <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
                    {formatDateTime(asset.createdAt)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}

// ── Shared bits ──────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const tone = getJobStatusTone(status);
  const Icon = isFailedStatus(status) ? XCircle : isPartialStatus(status) ? AlertTriangle : CheckCircle2;
  return (
    <Badge variant={tone} className="w-fit">
      <Icon className="mr-1 h-3 w-3" /> {status}
    </Badge>
  );
}

function IdValue({ label, value, block }: { label: string; value: string; block?: boolean }) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard access can be denied by the browser; the full id is still visible via title.
    }
  }

  if (block) {
    return (
      <div className="rounded-xl border border-border bg-background/40 p-3">
        <p className="text-[0.65rem] uppercase tracking-widest text-muted-foreground">{label}</p>
        <div className="mt-1 flex items-center gap-1.5">
          <span className="truncate font-mono text-xs" title={value}>
            {value}
          </span>
          <button
            type="button"
            onClick={copy}
            className="shrink-0 text-muted-foreground hover:text-foreground"
            title="Copy full id"
          >
            {copied ? <CheckCircle2 className="h-3.5 w-3.5 text-emerald-500" /> : <Copy className="h-3.5 w-3.5" />}
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex items-center gap-1 text-muted-foreground">
      <span className="w-16 shrink-0">{label}:</span>
      <span className="truncate font-mono" title={value}>
        {shortId(value)}
      </span>
    </div>
  );
}

function shortId(value: string) {
  return value.length > 13 ? `${value.slice(0, 8)}…${value.slice(-4)}` : value;
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit"
  }).format(new Date(value));
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(new Date(value));
}

function formatJobDuration(startedAt: string | null, completedAt: string | null) {
  if (!startedAt || !completedAt) {
    return "—";
  }
  const seconds = (new Date(completedAt).getTime() - new Date(startedAt).getTime()) / 1000;
  return formatDurationSeconds(seconds);
}
