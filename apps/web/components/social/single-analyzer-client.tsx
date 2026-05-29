"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { useRouter } from "next/navigation";
import {
  ScanSearch,
  Search,
  Download,
  Clock,
  RefreshCw,
  Film,
  ImageIcon,
  Megaphone,
  CheckCircle2,
  ChevronRight,
  Sparkles,
  Package,
  Trash2,
  XCircle,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { BlockingLoader } from "@/components/ui/blocking-loader";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { getMockClients, getMockOrganizations } from "@/lib/mocks/content-workflow";
import { listWorkflowClients } from "@/lib/api/content-workflow";
import type {
  Organization,
  SingleContentAnalysisStatus,
  SingleContentAnalysisHistoryItem,
  SocialClient,
  SocialNetwork,
  ContentFormatKey,
  ContentGenerationJobStatus,
} from "@/lib/api/types";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { listSocialNetworks } from "@/lib/api/social-networks";
import { useAccessToken } from "@/components/auth/use-access-token";
import {
  getMockVideoBySearch,
  analyzeMockVideo,
  type SingleAnalyzerResult,
} from "@/lib/mocks/single-analyzer";
import {
  analyzeSingleContent,
  deleteSingleContentAnalysis,
  getSingleContentAnalysis,
  listSingleContentAnalyses,
  mapBackendResponseToResult,
  retrySingleContentAnalysisAi,
} from "@/lib/api/single-analyzer";
import {
  requestPackage,
  downloadPackageFile,
  ALL_FORMAT_KEYS,
  FORMAT_LABELS,
} from "@/lib/api/content-generation";
import { useContentGenerationJob } from "@/lib/hooks/use-content-generation-job";
import { InferredFieldsDisclosure } from "@/components/social/single-analyzer/inferred-fields-disclosure";
import { PartialCompletionBanner } from "@/components/social/single-analyzer/partial-completion-banner";
import { SupportError } from "@/components/support/support-error";

// ── Async/poll constants ───────────────────────────────────────────────────────

const POLL_INTERVAL_MS = 2500;
const POLL_TIMEOUT_MS = 5 * 60 * 1000; // 5 min hard cap so the overlay can't hang forever

// Ordered stages shown in the progress overlay — mapped to backend statuses.
const ANALYSIS_STEPS: Array<{
  key: string;
  label: string;
  statuses: SingleContentAnalysisStatus[];
}> = [
  { key: "queued", label: "Queued", statuses: ["Requested"] },
  { key: "fetch", label: "Fetching content", statuses: ["ResolvingContent"] },
  { key: "ready", label: "Content ready", statuses: ["ContentResolved", "ContentResolvedPartial"] },
  { key: "ai", label: "AI analysis", statuses: ["Analyzing"] },
];

function statusToPercent(status: SingleContentAnalysisStatus | null): number {
  switch (status) {
    case "Requested": return 8;
    case "ResolvingContent": return 35;
    case "ContentResolved":
    case "ContentResolvedPartial": return 62;
    case "Analyzing": return 85;
    case "Analyzed": return 100;
    case "Failed": return 100;
    default: return 0;
  }
}

function statusToStepIndex(status: SingleContentAnalysisStatus | null): number {
  if (!status) return 0;
  if (status === "Analyzed" || status === "Failed") return ANALYSIS_STEPS.length;
  const idx = ANALYSIS_STEPS.findIndex((s) => s.statuses.includes(status));
  return idx === -1 ? 0 : idx;
}

const PACKAGE_STEPS = [
  { key: "queued", label: "Queued" },
  { key: "prepare", label: "Preparing context" },
  { key: "ai", label: "Generating formats" },
  { key: "render", label: "Rendering assets" },
  { key: "zip", label: "Building package.zip" },
] as const;

function isAiCheckpointStatus(status: string): boolean {
  return (
    status === "AiSucceeded" ||
    status === "AiFailed" ||
    status === "Rendered" ||
    status === "RenderFailed" ||
    status === "Succeeded" ||
    status === "Failed"
  );
}

function isRenderCheckpointStatus(status: string): boolean {
  return (
    status === "Rendered" ||
    status === "RenderFailed" ||
    status === "Succeeded" ||
    status === "Failed"
  );
}

function packageProgressState(
  status: ContentGenerationJobStatus | null,
  perFormatStatus: Partial<Record<ContentFormatKey, string>>,
  totalFormats: number
): { percent: number; label: string; currentStep: number; completedFormats: number } {
  const total = Math.max(totalFormats, 1);
  const statuses = Object.values(perFormatStatus);
  const aiDone = statuses.filter(isAiCheckpointStatus).length;
  const renderDone = statuses.filter(isRenderCheckpointStatus).length;

  if (!status) {
    return {
      percent: 12,
      label: "Initializing generation request",
      currentStep: 1,
      completedFormats: 0,
    };
  }

  if (status === "Queued") {
    return { percent: 8, label: "Queued", currentStep: 0, completedFormats: 0 };
  }

  if (status === "Running") {
    if (aiDone === 0 && renderDone === 0) {
      return {
        percent: 18,
        label: "Preparing analysis context",
        currentStep: 1,
        completedFormats: 0,
      };
    }

    if (renderDone === 0) {
      const percent = Math.round(22 + (Math.min(aiDone, total) / total) * 36);
      return {
        percent,
        label: `Generating formats with AI (${Math.min(aiDone, total)}/${total})`,
        currentStep: 2,
        completedFormats: 0,
      };
    }

    if (renderDone < total) {
      const percent = Math.round(60 + (renderDone / total) * 30);
      return {
        percent,
        label: `Rendering assets (${renderDone}/${total})`,
        currentStep: 3,
        completedFormats: renderDone,
      };
    }

    return {
      percent: 95,
      label: "Building package.zip",
      currentStep: 4,
      completedFormats: total,
    };
  }

  if (status === "Completed") {
    return {
      percent: 100,
      label: "All formats generated",
      currentStep: PACKAGE_STEPS.length,
      completedFormats: total,
    };
  }

  if (status === "Partial" || status === "PartialSuccess") {
    return {
      percent: 100,
      label: "Completed with partial results",
      currentStep: PACKAGE_STEPS.length,
      completedFormats: Math.min(renderDone, total),
    };
  }

  return {
    percent: 100,
    label: "Generation failed",
    currentStep: Math.min(4, PACKAGE_STEPS.length - 1),
    completedFormats: Math.min(renderDone, total),
  };
}

// ── URL → social platform detector ────────────────────────────────────────────
// Mirrors Domain.SingleAnalyzerSourceUrl.DetectPlatform so we can show the user the detected
// network BEFORE submitting. The backend is still the source of truth — this is UX-only.
type DetectedPlatform =
  | "tiktok"
  | "instagram"
  | "youtube"
  | "x"
  | "facebook"
  | "linkedin"
  | "unknown";

const PLATFORM_LABELS: Record<DetectedPlatform, string> = {
  tiktok: "TikTok",
  instagram: "Instagram",
  youtube: "YouTube",
  x: "X / Twitter",
  facebook: "Facebook",
  linkedin: "LinkedIn",
  unknown: "Unknown",
};

function detectPlatformFromUrl(raw: string): DetectedPlatform | null {
  const url = raw.trim();
  if (!url) return null;
  let host: string;
  try {
    host = new URL(url).hostname.toLowerCase();
  } catch {
    return null;
  }
  if (host.startsWith("www.")) host = host.slice(4);

  if (host === "tiktok.com" || host === "vm.tiktok.com" || host === "m.tiktok.com" || host.endsWith(".tiktok.com")) return "tiktok";
  if (host === "instagram.com" || host === "instagr.am" || host.endsWith(".instagram.com")) return "instagram";
  if (host === "youtube.com" || host === "youtu.be" || host === "youtube-nocookie.com" || host.endsWith(".youtube.com")) return "youtube";
  if (host === "x.com" || host === "twitter.com" || host.endsWith(".x.com") || host.endsWith(".twitter.com")) return "x";
  if (host === "facebook.com" || host === "fb.com" || host === "fb.watch" || host === "m.facebook.com" || host.endsWith(".facebook.com")) return "facebook";
  if (host === "linkedin.com" || host === "lnkd.in" || host.endsWith(".linkedin.com")) return "linkedin";
  return "unknown";
}

// ── Constants ──────────────────────────────────────────────────────────────────

const strategyOptions = [
  { value: "educational", label: "Educational" },
  { value: "informative", label: "Informative" },
  { value: "entertainment", label: "Entertainment" },
];

function strategyBadgeVariant(strategy: string): BadgeVariant {
  switch (strategy) {
    case "educational":
      return "info";
    case "informative":
      return "accent";
    case "entertainment":
      return "warning";
    default:
      return "muted";
  }
}

function sleep(ms: number) {
  return new Promise<void>((resolve) => setTimeout(resolve, ms));
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

function formatDuration(seconds: number | null | undefined): string {
  if (typeof seconds !== "number" || seconds <= 0) return "—";
  const mins = Math.floor(seconds / 60);
  const secs = Math.round(seconds % 60);
  return `${mins}:${secs.toString().padStart(2, "0")}`;
}

// ── Local sub-components ───────────────────────────────────────────────────────

function Fact({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-xs text-muted-foreground/60 uppercase tracking-wider">
        {label}
      </span>
      <span className="text-sm font-medium text-foreground/90">{value}</span>
    </div>
  );
}

type TabOption = { key: string; label: string };

function TabBar({
  tabs,
  active,
  onChange,
}: {
  tabs: TabOption[];
  active: string;
  onChange: (key: string) => void;
}) {
  return (
    <div className="flex items-center gap-1 rounded-lg bg-muted/40 p-1 w-fit">
      {tabs.map((tab) => (
        <button
          key={tab.key}
          type="button"
          onClick={() => onChange(tab.key)}
          className={cn(
            "rounded-md px-3 py-1.5 text-xs font-medium transition-colors",
            active === tab.key
              ? "bg-background text-foreground shadow-sm"
              : "text-muted-foreground hover:text-foreground"
          )}
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}

function InlineProgress({ value }: { value: number }) {
  return (
    <div className="h-1 w-full rounded-full bg-muted overflow-hidden">
      <div
        className="h-full bg-primary transition-all duration-150"
        style={{ width: `${Math.min(100, Math.max(0, value))}%` }}
      />
    </div>
  );
}

function AnalysisOverlay({ status }: { status: SingleContentAnalysisStatus | null }) {
  if (!status) return null;
  const percent = statusToPercent(status);
  const currentStep = statusToStepIndex(status);
  const currentLabel = ANALYSIS_STEPS[Math.min(currentStep, ANALYSIS_STEPS.length - 1)]?.label ?? "Working…";

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Analyzing content"
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 backdrop-blur-sm"
    >
      <div className="w-full max-w-md rounded-2xl border border-border/50 bg-card p-6 shadow-2xl space-y-5">
        <div className="flex items-center gap-3">
          <div className="rounded-lg bg-purple-500/10 p-2">
            <ScanSearch className="h-5 w-5 text-purple-400 animate-pulse" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-foreground">Analyzing content</h3>
            <p className="text-xs text-muted-foreground">
              This usually takes 30–60 seconds. Keep this tab open.
            </p>
          </div>
        </div>

        <div className="space-y-2">
          <div className="flex items-center justify-between text-xs">
            <span className="text-muted-foreground">{currentLabel}</span>
            <span className="font-medium text-foreground tabular-nums">{percent}%</span>
          </div>
          <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
            <div
              className="h-full rounded-full bg-gradient-to-r from-purple-500 to-blue-500 transition-all duration-500 ease-out"
              style={{ width: `${percent}%` }}
            />
          </div>
        </div>

        <ol className="space-y-2">
          {ANALYSIS_STEPS.map((step, i) => {
            const state = i < currentStep ? "done" : i === currentStep ? "active" : "pending";
            return (
              <li key={step.key} className="flex items-center gap-2.5 text-xs">
                <span
                  className={cn(
                    "flex h-5 w-5 items-center justify-center rounded-full border",
                    state === "done" && "border-green-500/50 bg-green-500/15 text-green-500",
                    state === "active" && "border-purple-500/50 bg-purple-500/15 text-purple-400",
                    state === "pending" && "border-border/50 bg-muted/40 text-muted-foreground/50"
                  )}
                >
                  {state === "done" ? (
                    <CheckCircle2 className="h-3 w-3" />
                  ) : state === "active" ? (
                    <RefreshCw className="h-3 w-3 animate-spin" />
                  ) : (
                    <span className="h-1.5 w-1.5 rounded-full bg-current opacity-50" />
                  )}
                </span>
                <span
                  className={cn(
                    state === "pending" ? "text-muted-foreground/50" : "text-foreground/90"
                  )}
                >
                  {step.label}
                </span>
              </li>
            );
          })}
        </ol>
      </div>
    </div>
  );
}

function PackageGenerationOverlay({
  visible,
  status,
  perFormatStatus,
  totalFormats,
}: {
  visible: boolean;
  status: ContentGenerationJobStatus | null;
  perFormatStatus: Partial<Record<ContentFormatKey, string>>;
  totalFormats: number;
}) {
  if (!visible) return null;

  const state = packageProgressState(status, perFormatStatus, totalFormats);

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Generating content package"
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/85 backdrop-blur-sm"
    >
      <div className="w-full max-w-md rounded-2xl border border-border/50 bg-card p-6 shadow-2xl space-y-5">
        <div className="flex items-center gap-3">
          <div className="rounded-lg bg-primary/10 p-2">
            <Package className="h-5 w-5 text-primary animate-pulse" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-foreground">Generating content package</h3>
            <p className="text-xs text-muted-foreground">
              Keep this screen open while all formats are processed.
            </p>
          </div>
        </div>

        <div className="space-y-2">
          <div className="flex items-center justify-between text-xs">
            <span className="text-muted-foreground">{state.label}</span>
            <span className="font-medium text-foreground tabular-nums">{state.percent}%</span>
          </div>
          <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
            <div
              className="h-full rounded-full bg-gradient-to-r from-blue-500 via-purple-500 to-primary transition-all duration-500 ease-out"
              style={{ width: `${Math.min(100, Math.max(0, state.percent))}%` }}
            />
          </div>
          <p className="text-[11px] text-muted-foreground/80">
            Rendered formats: {state.completedFormats}/{Math.max(totalFormats, 1)}
          </p>
        </div>

        <ol className="space-y-2">
          {PACKAGE_STEPS.map((step, i) => {
            const markerState =
              i < state.currentStep ? "done" : i === state.currentStep ? "active" : "pending";

            return (
              <li key={step.key} className="flex items-center gap-2.5 text-xs">
                <span
                  className={cn(
                    "flex h-5 w-5 items-center justify-center rounded-full border",
                    markerState === "done" && "border-green-500/50 bg-green-500/15 text-green-500",
                    markerState === "active" && "border-primary/50 bg-primary/15 text-primary",
                    markerState === "pending" &&
                      "border-border/50 bg-muted/40 text-muted-foreground/50"
                  )}
                >
                  {markerState === "done" ? (
                    <CheckCircle2 className="h-3 w-3" />
                  ) : markerState === "active" ? (
                    <RefreshCw className="h-3 w-3 animate-spin" />
                  ) : (
                    <span className="h-1.5 w-1.5 rounded-full bg-current opacity-50" />
                  )}
                </span>
                <span
                  className={cn(
                    markerState === "pending" ? "text-muted-foreground/50" : "text-foreground/90"
                  )}
                >
                  {step.label}
                </span>
              </li>
            );
          })}
        </ol>
      </div>
    </div>
  );
}

function SocialTabletPreview({
  content,
  network,
}: {
  content: string;
  network: string;
}) {
  const lines = content.split("\n").filter(Boolean);
  return (
    <div className="rounded-xl border border-border/40 bg-background/60 p-4 space-y-3 min-h-[200px]">
      <div className="flex items-center gap-2 pb-2 border-b border-border/30">
        <div className="w-8 h-8 rounded-full bg-gradient-to-br from-purple-500/40 to-blue-500/40 flex items-center justify-center">
          <span className="text-xs font-bold text-foreground/80">
            {network.charAt(0).toUpperCase()}
          </span>
        </div>
        <span className="text-xs font-medium text-muted-foreground capitalize">
          {network}
        </span>
      </div>
      <div className="space-y-1.5">
        {lines.slice(0, 8).map((line, i) => (
          <p key={i} className="text-sm text-foreground/80 leading-relaxed">
            {line}
          </p>
        ))}
        {lines.length > 8 && (
          <p className="text-xs text-muted-foreground/60 italic">
            +{lines.length - 8} more lines…
          </p>
        )}
      </div>
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

interface SingleAnalyzerClientProps {
  isPlatformAdmin: boolean;
}

export function SingleAnalyzerClient({ isPlatformAdmin }: SingleAnalyzerClientProps) {
  const router = useRouter();
  const { orgId: activeOrgId } = useActiveOrg();
  const token = useAccessToken();
  const effectiveToken = token ?? undefined;

  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState("");
  const [clients, setClients] = useState<SocialClient[]>([]);
  const [selectedClientId, setSelectedClientId] = useState("");
  const [clientsLoading, setClientsLoading] = useState(false);
  const [socialNetworks, setSocialNetworks] = useState<SocialNetwork[]>([]);
  const [networksLoading, setNetworksLoading] = useState(true);
  const [inputMode, setInputMode] = useState<"url" | "search">("url");
  const [urlInput, setUrlInput] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [searchNetwork, setSearchNetwork] = useState("");
  const [searchStrategy, setSearchStrategy] = useState("educational");
  const [videoResult, setVideoResult] = useState<SingleAnalyzerResult | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [isAnalyzed, setIsAnalyzed] = useState(false);
  const [analyzingProgress, setAnalyzingProgress] = useState(0);
  const [generatedScripts, setGeneratedScripts] = useState<Record<string, string>>({});
  const [generatedCarousels, setGeneratedCarousels] = useState<Record<string, string>>({});
  const [generatedFlyers, setGeneratedFlyers] = useState<Record<string, string>>({});
  const [generatingConversion, setGeneratingConversion] = useState<string | null>(null);
  const [rightPanelTab, setRightPanelTab] = useState<"conversion" | "preview">("conversion");
  const [previewNetworkTab, setPreviewNetworkTab] = useState("");
  const [mutationMode, setMutationMode] = useState<"script" | "carousel" | "flyer">("script");
  const [historyItems, setHistoryItems] = useState<SingleContentAnalysisHistoryItem[]>([]);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [deletingHistoryId, setDeletingHistoryId] = useState<string | null>(null);
  // When loading from history, we want the new analysis's existing content-generation job to
  // re-attach to the convert panel. The packageJobId-reset effect runs AFTER setVideoResult,
  // so we pre-stage the jobId here and the effect reads it as it runs.
  const pendingPackageJobIdRef = useRef<string | null>(null);
  const [pendingNavigationHref, setPendingNavigationHref] = useState<string | null>(null);
  const [isAbandonDialogOpen, setIsAbandonDialogOpen] = useState(false);
  const allowNavigationRef = useRef(false);
  const [isDownloading, setIsDownloading] = useState(false);
  const [isDownloadingPackage, setIsDownloadingPackage] = useState(false);
  const [isHydratingHistory, setIsHydratingHistory] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState(0);
  const [downloadResult, setDownloadResult] = useState<{ fileName: string; downloadUrl: string } | null>(null);
  const [error, setError] = useState<{ code?: string; message: string } | null>(null);
  // Async/poll state: drives the staged progress overlay during a backend analysis.
  const [analysisStatus, setAnalysisStatus] = useState<SingleContentAnalysisStatus | null>(null);
  const pollCancelRef = useRef(false);
  const lastHistoryRefreshKeyRef = useRef<string | null>(null);

  // Content-generation package job state.
  const [packageJobId, setPackageJobId] = useState<string | null>(null);
  const [packageJobError, setPackageJobError] = useState<{ message: string } | null>(null);
  const [isRequestingPackage, setIsRequestingPackage] = useState(false);

  // UX state for the new agregados.
  const [isVideoAnalysisCollapsed, setIsVideoAnalysisCollapsed] = useState(false);

  // Live-detected network from the URL the user is typing (frontend hint; backend is authoritative).
  const detectedPlatform = useMemo(() => detectPlatformFromUrl(urlInput), [urlInput]);

  // Package generation polling — active while packageJobId is set and no terminal state reached.
  const packageJob = useContentGenerationJob(packageJobId, {
    token: effectiveToken,
    enabled: packageJobId !== null,
  });

  const packageUiStatus: ContentGenerationJobStatus | null =
    packageJob.status ?? (isRequestingPackage ? "Queued" : null);

  // Package job derived UI state.
  const packageGenerationIsRunning =
    isRequestingPackage ||
    packageJob.isLoading ||
    packageJob.isPolling ||
    packageUiStatus === "Queued" ||
    packageUiStatus === "Running";
  const packageGenerationIsCompleted = packageUiStatus === "Completed";
  const packageGenerationIsPartial =
    packageUiStatus === "Partial" || packageUiStatus === "PartialSuccess";
  const packageGenerationIsFailed = packageUiStatus === "Failed";
  const packageDownloadAvailable =
    (packageGenerationIsCompleted || packageGenerationIsPartial) && packageJobId !== null;
  const hasUnsavedWork = Boolean(videoResult && isAnalyzed);
  const initialToolLoading =
    networksLoading ||
    historyLoading ||
    (selectedOrganizationId.length > 0 && clientsLoading && clients.length === 0);

  const packageSucceededFormats = useMemo(
    () =>
      (Object.entries(packageJob.perFormatStatus)
        .filter(([, st]) => st === "Rendered" || st === "Succeeded")
        .map(([fmt]) => fmt) as ContentFormatKey[]),
    [packageJob.perFormatStatus]
  );
  const packageFailedFormats = useMemo(
    () =>
      (Object.entries(packageJob.perFormatStatus)
        .filter(([, st]) => st === "Failed")
        .map(([fmt]) => fmt) as ContentFormatKey[]),
    [packageJob.perFormatStatus]
  );

  const handlePackageDownload = useCallback(async () => {
    if (!packageJobId || isDownloadingPackage) return;
    setIsDownloadingPackage(true);
    setPackageJobError(null);
    try {
      const { blob, filename } = await downloadPackageFile(packageJobId, { token: effectiveToken });
      const objectUrl = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = objectUrl;
      a.download = filename;
      a.rel = "noopener";
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
    } catch (e) {
      const err = e as { message?: string };
      setPackageJobError({ message: err?.message ?? "Failed to download package.zip." });
    } finally {
      setIsDownloadingPackage(false);
    }
  }, [packageJobId, isDownloadingPackage, effectiveToken]);

  // True when the URL currently typed is the SAME we just analyzed — blocks accidental re-runs
  // that would burn extraction tokens for no gain. Reactivates as soon as the URL changes.
  const isCurrentUrlAlreadyAnalyzed = useMemo(
    () =>
      isAnalyzed &&
      !!videoResult &&
      videoResult.inputUrl.trim() === urlInput.trim() &&
      urlInput.trim().length > 0,
    [isAnalyzed, videoResult, urlInput]
  );

  // Cancel any in-flight polling loop on unmount.
  useEffect(() => {
    return () => {
      pollCancelRef.current = true;
    };
  }, []);

  // Guard against closing/reloading while there is work on screen.
  useEffect(() => {
    if (!hasUnsavedWork && !packageGenerationIsRunning) return;
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      // Modern browsers ignore the message and show their own; setting returnValue is required
      // to trigger the confirmation dialog at all.
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [hasUnsavedWork, packageGenerationIsRunning]);

  // Themed in-app abandonment guard for navigation clicks (sidebar/menu links).
  useEffect(() => {
    if (!hasUnsavedWork || packageGenerationIsRunning) return;

    const handleDocumentNavigation = (event: MouseEvent) => {
      if (allowNavigationRef.current) return;
      if (event.defaultPrevented) return;
      if (event.button !== 0) return;
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

      const target = event.target as HTMLElement | null;
      const anchor = target?.closest("a[href]") as HTMLAnchorElement | null;
      if (!anchor) return;
      if (anchor.target && anchor.target !== "_self") return;
      if (anchor.hasAttribute("download")) return;

      const href = anchor.getAttribute("href");
      if (!href || href.startsWith("#") || href.startsWith("javascript:")) return;

      const currentUrl = new URL(window.location.href);
      const nextUrl = new URL(href, currentUrl.href);
      if (nextUrl.origin !== currentUrl.origin) return;

      const currentRoute = `${currentUrl.pathname}${currentUrl.search}${currentUrl.hash}`;
      const nextRoute = `${nextUrl.pathname}${nextUrl.search}${nextUrl.hash}`;
      if (nextRoute === currentRoute) return;

      event.preventDefault();
      event.stopPropagation();
      setPendingNavigationHref(nextRoute);
      setIsAbandonDialogOpen(true);
    };

    document.addEventListener("click", handleDocumentNavigation, true);
    return () => document.removeEventListener("click", handleDocumentNavigation, true);
  }, [hasUnsavedWork, packageGenerationIsRunning]);

  // Derived — always in sync with API data
  const networkOptions = socialNetworks.map((n) => ({ value: n.code, label: n.displayName }));
  const selectedClient = useMemo(
    () => clients.find((client) => client.id === selectedClientId) ?? null,
    [clients, selectedClientId]
  );

  const loadNetworks = useCallback(async () => {
    if (!effectiveToken) return;
    setNetworksLoading(true);
    try {
      const result = await listSocialNetworks({ page: 1, pageSize: 100 }, effectiveToken);
      setSocialNetworks(result.items);
      if (result.items.length > 0) {
        setSearchNetwork((prev) =>
          result.items.some((n) => n.code === prev) ? prev : result.items[0].code
        );
        setPreviewNetworkTab((prev) =>
          result.items.some((n) => n.code === prev) ? prev : result.items[0].code
        );
      }
    } catch {
      // silent — select stays disabled until retry
    } finally {
      setNetworksLoading(false);
    }
  }, [effectiveToken]);

  useEffect(() => {
    void loadNetworks();
  }, [loadNetworks]);

  const loadClients = useCallback(async () => {
    if (!selectedOrganizationId) {
      setClients([]);
      setSelectedClientId("");
      return;
    }

    setClientsLoading(true);
    try {
      const result = effectiveToken
        ? (await listWorkflowClients(selectedOrganizationId, effectiveToken)).items
        : getMockClients(selectedOrganizationId);
      const activeClients = result.filter((client) => client.isActive);
      setClients(activeClients);
      setSelectedClientId((current) =>
        current && activeClients.some((client) => client.id === current) ? current : ""
      );
    } catch {
      const activeClients = getMockClients(selectedOrganizationId).filter((client) => client.isActive);
      setClients(activeClients);
      setSelectedClientId((current) =>
        current && activeClients.some((client) => client.id === current) ? current : ""
      );
    } finally {
      setClientsLoading(false);
    }
  }, [effectiveToken, selectedOrganizationId]);

  useEffect(() => {
    void loadClients();
  }, [loadClients]);

  useEffect(() => {
    const orgs = getMockOrganizations();
    setOrganizations(orgs);
    if (orgs.length > 0) setSelectedOrganizationId(orgs[0].id);
  }, []);

  const loadHistory = useCallback(async (opts?: { silent?: boolean }) => {
    if (!effectiveToken) {
      setHistoryLoading(false);
      return;
    }

    if (!opts?.silent) {
      setHistoryLoading(true);
    }

    try {
      const result = await listSingleContentAnalyses(1, 20, effectiveToken);
      setHistoryItems(result.items);
    } catch {
      // Silent: keep the previous list rather than wipe the grid on a transient blip. A failure
      // banner here would be noisy for a peripheral panel that doesn't block the main flow.
    } finally {
      if (!opts?.silent) {
        setHistoryLoading(false);
      }
    }
  }, [effectiveToken]);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory, selectedOrganizationId]);

  useEffect(() => {
    if (!isPlatformAdmin && activeOrgId) {
      setSelectedOrganizationId(activeOrgId);
    }
  }, [isPlatformAdmin, activeOrgId]);

  function resetVideoState() {
    setVideoResult(null);
    setIsAnalyzed(false);
    setDownloadResult(null);
    setGeneratedScripts({});
    setGeneratedCarousels({});
    setGeneratedFlyers({});
    setError(null);
    setPackageJobId(null);
    setPackageJobError(null);
  }

  async function loadVideoByUrl() {
    if (!urlInput.trim()) return;
    setIsLoading(true);
    resetVideoState();
    setAnalysisStatus("Requested");
    pollCancelRef.current = false;
    try {
      // ASYNC/POLL: POST queues the work and returns immediately with the analysis id/status.
      // We then poll until Analyzed or Failed. The browser never contacts Apify directly
      // (CAP5-MUST-01/03) and the long extraction + AI run is decoupled from this request,
      // so a slow client connection never cancels the backend work.
      const started = await analyzeSingleContent(
        {
          url: urlInput.trim(),
          clientId: selectedClientId || undefined,
        },
        effectiveToken
      );
      setAnalysisStatus(started.status);

      const final = await pollUntilTerminal(started.id);
      if (!final) return; // cancelled (component unmounted)

      if (final.status === "Failed") {
        // Partial success path: if the extraction stage completed (snapshot present), hydrate the
        // video card with that metadata even though the AI stage failed. The workflow nodes are
        // isolated — losing AI analysis must NOT discard real data we already obtained from Apify.
        // The error banner explains what failed; the right panel stays in empty-state because
        // there's no analysis to drive the generation tabs.
        if (final.snapshot) {
          const partial = mapBackendResponseToResult(final, selectedOrganizationId);
          setVideoResult(partial);
          setPreviewNetworkTab(partial.networkSource);
        }
        setError({
          code: final.failureCode ?? "social.single_analyzer.analysis_failed",
          message: final.failureMessageKey ?? "Analysis failed. Please try again.",
        });
        return;
      }

      const result = mapBackendResponseToResult(final, selectedOrganizationId);
      setVideoResult(result);
      setPreviewNetworkTab(result.networkSource);
      setIsAnalyzed(true);
      void loadHistory({ silent: true });
    } catch (e) {
      const err = e as { message?: string; problem?: Record<string, unknown> };
      const code = typeof err.problem?.code === "string" ? err.problem.code : undefined;
      setError({
        code,
        message:
          err?.message ||
          "Analysis failed. The URL may be unsupported or the backend took too long. Try again.",
      });
    } finally {
      setIsLoading(false);
      setAnalysisStatus(null);
    }
  }

  async function retryAiAnalysis(id: string) {
    setIsLoading(true);
    setError(null);
    setAnalysisStatus("Analyzing");
    pollCancelRef.current = false;
    try {
      // POST retry-ai: re-runs ONLY the AI stage on the backend using the persisted snapshot.
      // No Apify call, no extra extraction tokens.
      const started = await retrySingleContentAnalysisAi(id, effectiveToken);
      setAnalysisStatus(started.status);

      const final = await pollUntilTerminal(id);
      if (!final) return; // cancelled (component unmounted)

      if (final.status === "Failed") {
        // Snapshot is still on screen from the previous run; just refresh the error banner.
        setError({
          code: final.failureCode ?? "social.single_analyzer.analysis_failed",
          message: final.failureMessageKey ?? "Analysis failed. Please try again.",
        });
        return;
      }

      const result = mapBackendResponseToResult(final, selectedOrganizationId);
      setVideoResult(result);
      setPreviewNetworkTab(result.networkSource);
      setIsAnalyzed(true);
      void loadHistory({ silent: true });
    } catch (e) {
      const err = e as { message?: string; problem?: Record<string, unknown> };
      const code = typeof err.problem?.code === "string" ? err.problem.code : undefined;
      setError({
        code,
        message: err?.message || "Retry failed. Please try again.",
      });
    } finally {
      setIsLoading(false);
      setAnalysisStatus(null);
    }
  }

  async function pollUntilTerminal(id: string) {
    const deadline = Date.now() + POLL_TIMEOUT_MS;
    while (Date.now() < deadline) {
      if (pollCancelRef.current) return null;
      await sleep(POLL_INTERVAL_MS);
      if (pollCancelRef.current) return null;
      const current = await getSingleContentAnalysis(id, effectiveToken);
      setAnalysisStatus(current.status);
      if (current.status === "Analyzed" || current.status === "Failed") {
        return current;
      }
    }
    // Hard timeout — surface as a generic failed analysis so the overlay can close.
    throw new Error(
      "The analysis is taking longer than expected. Please refresh and try again."
    );
  }

  async function findBestMatch() {
    if (!searchQuery.trim()) return;
    setIsLoading(true);
    resetVideoState();
    await sleep(700);
    try {
      const result = getMockVideoBySearch(
        selectedOrganizationId,
        searchQuery.trim(),
        searchNetwork,
        searchStrategy
      );
      setVideoResult(result);
      setPreviewNetworkTab(result.networkSource);
    } catch {
      setError({ message: "Search failed. Try a different keyword." });
    } finally {
      setIsLoading(false);
    }
  }

  async function runAnalysis() {
    if (!videoResult) return;
    setIsAnalyzing(true);
    setAnalyzingProgress(0);
    setIsAnalyzed(false);
    const start = Date.now();
    const duration = 3000;
    const interval = setInterval(() => {
      const elapsed = Date.now() - start;
      setAnalyzingProgress(Math.min(Math.floor((elapsed / duration) * 100), 95));
    }, 80);
    await sleep(duration);
    clearInterval(interval);
    // Mock-only fallback path (no backend in this flow). Backend-driven runs persist on the server
    // and the history grid reloads from listSingleContentAnalyses — the in-memory mock buffer is
    // no longer the source of truth.
    const analyzed = analyzeMockVideo(selectedOrganizationId, videoResult.id);
    setAnalyzingProgress(100);
    await sleep(300);
    setVideoResult(analyzed);
    setIsAnalyzing(false);
    setIsAnalyzed(true);
  }

  function buildScriptContent(network: string) {
    if (!videoResult) return "";
    const clientLine = selectedClient
      ? `Client orientation: adapt this mutation for ${selectedClient.name}.`
      : "Client orientation: none - keep the original video's angle and positioning.";
    return [
      `── ${network.toUpperCase()} SCRIPT ──`,
      clientLine,
      "",
      videoResult.scriptDraft || "(Run analysis to generate script)",
      "",
      `Hook adapted for ${network}: lead with the strongest transformation.`,
      `CTA: Follow on ${network} for more insights.`,
    ].join("\n");
  }

  function buildCarouselContent(network: string) {
    if (!videoResult) return "";
    const clientLine = selectedClient
      ? `[ Client Orientation ] ${selectedClient.name}`
      : "[ Client Orientation ] None - preserve original positioning";
    const points = videoResult.videoAnalysis
      ? videoResult.videoAnalysis.split("\n").filter(Boolean).slice(1, 4).map((l) => `→ ${l}`)
      : ["→ (Analyze video to generate key points)"];
    return [
      `📱 ${network.toUpperCase()} CAROUSEL`,
      "",
      clientLine,
      "",
      "[ Slide 1 — Cover ]",
      videoResult.title,
      "",
      "[ Slides 2–4 — Key Points ]",
      ...points,
      "",
      "[ Final Slide — CTA ]",
      `Follow on ${network} for more quality content.`,
    ].join("\n");
  }

  function buildFlyerContent(network: string) {
    if (!videoResult) return "";
    const clientLine = selectedClient
      ? `CLIENT: ${selectedClient.name}`
      : "CLIENT: None - original video orientation";
    return [
      `🖼️ ${network.toUpperCase()} FLYER`,
      "══════════════",
      clientLine,
      "",
      `HEADLINE: ${videoResult.title.toUpperCase().slice(0, 50)}`,
      "",
      `AUTHOR: ${videoResult.author}`,
      `VIEWS: ${videoResult.views.toLocaleString()} · LIKES: ${videoResult.likes.toLocaleString()}`,
      "",
      "FOOTER: Follow us for weekly insights.",
    ].join("\n");
  }

  async function handleGenerate(network: string) {
    if (mutationMode === "script" && generatedScripts[network]) return;
    if (mutationMode === "carousel" && generatedCarousels[network]) return;
    if (mutationMode === "flyer" && generatedFlyers[network]) return;
    const key = `${network}-${mutationMode}`;
    setGeneratingConversion(key);
    await sleep(mutationMode === "carousel" ? 1100 : mutationMode === "flyer" ? 800 : 900);
    if (mutationMode === "script") {
      setGeneratedScripts((prev) => ({ ...prev, [network]: buildScriptContent(network) }));
    } else if (mutationMode === "carousel") {
      setGeneratedCarousels((prev) => ({ ...prev, [network]: buildCarouselContent(network) }));
    } else {
      setGeneratedFlyers((prev) => ({ ...prev, [network]: buildFlyerContent(network) }));
    }
    setGeneratingConversion(null);
  }

  /**
   * Start a real backend content-generation package job for all 7 formats.
   * Replaces the previous mock-based bulk generation.
   */
  async function handleConvertAll() {
    if (!videoResult || isRequestingPackage || packageGenerationIsCompleted) return;
    setIsRequestingPackage(true);
    setPackageJobId(null);
    setPackageJobError(null);
    try {
      const response = await requestPackage(
        videoResult.id,
        { requestedFormats: ALL_FORMAT_KEYS as ContentFormatKey[] },
        { token: effectiveToken }
      );
      setPackageJobId(response.jobId);
    } catch (e) {
      const err = e as { message?: string };
      setPackageJobError({ message: err?.message ?? "Failed to start content generation." });
    } finally {
      setIsRequestingPackage(false);
    }
  }

  // Reset package job state when the analysis changes. If we're loading from history and the
  // user picked a row that already has a content-generation job, the click handler stashes that
  // jobId in pendingPackageJobIdRef so the convert panel reattaches instead of "starting fresh".
  useEffect(() => {
    setPackageJobId(pendingPackageJobIdRef.current);
    pendingPackageJobIdRef.current = null;
    setPackageJobError(null);
  }, [videoResult?.id]);

  // Keep the "Saved Analyses" row aligned with live package-job polling for the loaded analysis.
  useEffect(() => {
    if (!videoResult?.id || !packageJobId || !packageUiStatus) return;

    const isTerminal =
      packageUiStatus === "Completed" ||
      packageUiStatus === "Partial" ||
      packageUiStatus === "PartialSuccess" ||
      packageUiStatus === "Failed";
    const hasPackage =
      packageUiStatus === "Completed" ||
      packageUiStatus === "Partial" ||
      packageUiStatus === "PartialSuccess";

    setHistoryItems((prev) =>
      prev.map((row) => {
        if (row.id !== videoResult.id) return row;

        const previous = row.latestContentGeneration;
        const completedAt = isTerminal
          ? previous?.completedAt ?? new Date().toISOString()
          : previous?.completedAt ?? null;

        if (
          previous &&
          previous.jobId === packageJobId &&
          previous.status === packageUiStatus &&
          previous.hasPackage === hasPackage &&
          previous.completedAt === completedAt
        ) {
          return row;
        }

        return {
          ...row,
          latestContentGeneration: {
            jobId: packageJobId,
            status: packageUiStatus,
            hasPackage,
            completedAt,
          },
        };
      })
    );
  }, [videoResult?.id, packageJobId, packageUiStatus]);

  // On terminal transition, refetch once to keep row metadata (timestamps/package flags) exact.
  useEffect(() => {
    if (!packageJobId || !packageUiStatus) return;
    const isTerminal =
      packageUiStatus === "Completed" ||
      packageUiStatus === "Partial" ||
      packageUiStatus === "PartialSuccess" ||
      packageUiStatus === "Failed";
    if (!isTerminal) return;

    const refreshKey = `${packageJobId}:${packageUiStatus}`;
    if (lastHistoryRefreshKeyRef.current === refreshKey) return;
    lastHistoryRefreshKeyRef.current = refreshKey;
    void loadHistory({ silent: true });
  }, [packageJobId, packageUiStatus, loadHistory]);

  const closeAbandonDialog = useCallback(() => {
    setIsAbandonDialogOpen(false);
    setPendingNavigationHref(null);
  }, []);

  const confirmAbandonNavigation = useCallback(() => {
    if (!pendingNavigationHref) return;
    allowNavigationRef.current = true;
    const target = pendingNavigationHref;
    setIsAbandonDialogOpen(false);
    setPendingNavigationHref(null);
    resetVideoState();
    router.push(target);
  }, [pendingNavigationHref, router]);

  useEffect(() => {
    if (!isAbandonDialogOpen && !pendingNavigationHref) {
      allowNavigationRef.current = false;
    }
  }, [isAbandonDialogOpen, pendingNavigationHref]);

  /**
   * Click on a row of the history grid → fetch the full analysis from the backend and hydrate
   * the UI exactly as it would have looked when the analysis just completed (snapshot + analysis +
   * convert-panel state). No tokens are burned — we only read what's already persisted.
   *
   * If the analysis already had a content-generation job, that job's id is staged so the convert
   * panel re-attaches to it (showing Completed/Failed/etc. with download if a package exists).
   */
  const handleLoadFromHistory = useCallback(
    async (item: SingleContentAnalysisHistoryItem) => {
      if (!effectiveToken || isLoading) return;
      setIsHydratingHistory(true);
      setIsLoading(true);
      resetVideoState();
      setError(null);
      pendingPackageJobIdRef.current = item.latestContentGeneration?.jobId ?? null;
      try {
        const current = await getSingleContentAnalysis(item.id, effectiveToken);
        if (current.snapshot) {
          const result = mapBackendResponseToResult(current, selectedOrganizationId);
          setVideoResult(result);
          setPreviewNetworkTab(result.networkSource);
        }
        if (current.status === "Analyzed") {
          setIsAnalyzed(true);
        } else if (current.status === "Failed") {
          setError({
            code: current.failureCode ?? "social.single_analyzer.analysis_failed",
            message:
              current.failureMessageKey ??
              "This analysis failed previously. Retry the AI step to resume without re-scraping.",
          });
        }
        // For non-terminal statuses (Requested / ResolvingContent / Analyzing) we leave the
        // overlay off — the user can paste the URL again or just observe state changes via reload.
      } catch (e) {
        const err = e as { message?: string };
        setError({
          message: err?.message ?? "Failed to load this saved analysis. Please try again.",
        });
      } finally {
        setIsLoading(false);
        setIsHydratingHistory(false);
      }
    },
    [effectiveToken, isLoading, selectedOrganizationId]
  );

  const handleDeleteFromHistory = useCallback(
    async (item: SingleContentAnalysisHistoryItem) => {
      if (!effectiveToken) return;
      if (
        !window.confirm(
          "Delete this analysis? The scrape, AI analysis, any generated content and downloadable packages will be permanently removed."
        )
      ) {
        return;
      }
      setDeletingHistoryId(item.id);
      try {
        await deleteSingleContentAnalysis(item.id, effectiveToken);
        setHistoryItems((prev) => prev.filter((row) => row.id !== item.id));
        // Clear the main panel when the user just nuked the analysis they had loaded.
        if (videoResult?.id === item.id) {
          resetVideoState();
        }
      } catch (e) {
        const err = e as { message?: string };
        setError({ message: err?.message ?? "Failed to delete this analysis." });
      } finally {
        setDeletingHistoryId(null);
      }
    },
    [effectiveToken, videoResult?.id]
  );

  const handleHistoryDownload = useCallback(async (jobId: string) => {
    try {
      const { blob, filename } = await downloadPackageFile(jobId, { token: effectiveToken });
      const objectUrl = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = objectUrl;
      a.download = filename;
      a.rel = "noopener";
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
    } catch (e) {
      const err = e as { message?: string };
      setError({
        message: err?.message ?? "Failed to download this package.",
      });
    }
  }, [effectiveToken]);

  async function handleDownload() {
    if (!videoResult) return;
    setIsDownloading(true);
    setDownloadProgress(0);
    setDownloadResult(null);
    const start = Date.now();
    const interval = setInterval(() => {
      setDownloadProgress(Math.min(Math.floor(((Date.now() - start) / 2500) * 100), 90));
    }, 80);
    await sleep(2500);
    clearInterval(interval);
    // Video + analysis download: still a local simulation (no backend endpoint for this yet).
    const titleSlug = videoResult.title.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 48);
    const result = { fileName: `${titleSlug}-analysis.zip`, downloadUrl: `/mock-downloads/single/${titleSlug}-all.zip` };
    setDownloadProgress(100);
    await sleep(200);
    setDownloadResult(result);
    setIsDownloading(false);
  }

  function activeContent(network: string): string | null {
    if (mutationMode === "script") return generatedScripts[network] ?? null;
    if (mutationMode === "carousel") return generatedCarousels[network] ?? null;
    return generatedFlyers[network] ?? null;
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col gap-6 p-6 max-w-screen-xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="rounded-lg bg-purple-500/10 p-2.5">
            <ScanSearch className="h-5 w-5 text-purple-400" />
          </div>
          <div>
            <h1 className="text-xl font-semibold text-foreground">Single Analyzer</h1>
            <p className="text-sm text-muted-foreground">Load any video · analyze · convert</p>
          </div>
        </div>
        {isPlatformAdmin && organizations.length > 0 && (
          <Select
            size="sm"
            className="min-w-[12rem]"
            value={selectedOrganizationId}
            onChange={(e) => setSelectedOrganizationId(e.target.value)}
          >
            {organizations.map((org) => (
              <option key={org.id} value={org.id}>{org.name}</option>
            ))}
          </Select>
        )}
      </div>

      {/* Error banner with built-in support actions */}
      <SupportError
        error={error}
        context={{ feature: "single-analyzer", organizationId: selectedOrganizationId }}
        onDismiss={() => setError(null)}
      />

      {/* Screen-blocking overlay shown while the backend analysis is in flight. */}
      <BlockingLoader
        visible={initialToolLoading || isHydratingHistory}
        title={isHydratingHistory ? "Loading saved analysis" : "Preparing Single Analyzer"}
        description={
          isHydratingHistory
            ? "Recovering snapshot and analysis from history..."
            : "Loading organizations, clients, networks and saved analyses..."
        }
      />
      <AnalysisOverlay status={analysisStatus} />
      <PackageGenerationOverlay
        visible={packageGenerationIsRunning}
        status={packageUiStatus}
        perFormatStatus={packageJob.perFormatStatus}
        totalFormats={ALL_FORMAT_KEYS.length}
      />

      {/* Main 2-col grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* ── LEFT PANEL ── */}
        <div className="flex flex-col gap-4">
          {/* Input card */}
          <Card className="border-border/40 bg-background/60">
            <CardHeader className="pb-3">
              <TabBar
                tabs={[
                  { key: "url", label: "By URL" },
                  { key: "search", label: "By Search" },
                ]}
                active={inputMode}
                onChange={(k) => setInputMode(k as "url" | "search")}
              />
            </CardHeader>
            <CardContent className="pt-0 space-y-3">
              {inputMode === "url" ? (
                <div className="space-y-1.5">
                  <div className="flex gap-2">
                    <Input
                      placeholder="Paste a URL from TikTok, Instagram, YouTube, X, Facebook…"
                      value={urlInput}
                      onChange={(e) => setUrlInput(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter" && !isCurrentUrlAlreadyAnalyzed) void loadVideoByUrl();
                      }}
                      className="h-8 text-xs"
                    />
                    <Button
                      size="sm"
                      onClick={() => void loadVideoByUrl()}
                      disabled={isLoading || !urlInput.trim() || isCurrentUrlAlreadyAnalyzed}
                      className="h-8 px-3 text-xs"
                      title={
                        isCurrentUrlAlreadyAnalyzed
                          ? "This URL is already analyzed. Change the URL to enable Analyze."
                          : undefined
                      }
                    >
                      {isLoading ? (
                        <RefreshCw className="h-3 w-3 animate-spin" />
                      ) : isCurrentUrlAlreadyAnalyzed ? (
                        <span className="flex items-center gap-1">
                          <CheckCircle2 className="h-3 w-3" />
                          Analyzed
                        </span>
                      ) : (
                        "Analyze"
                      )}
                    </Button>
                  </div>
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-xs text-muted-foreground/70 flex-1">
                      {isLoading
                        ? "Analyzing… fetching and processing the video can take up to 60 seconds."
                        : isCurrentUrlAlreadyAnalyzed
                        ? "This URL has been analyzed. Paste a different URL to run a new analysis."
                        : "Paste a URL to scrape and analyze the video in one step."}
                    </p>
                    {detectedPlatform && (
                      <Badge
                        variant="outline"
                        className={cn(
                          "text-xs shrink-0 capitalize",
                          detectedPlatform === "unknown" && "border-amber-500/40 text-amber-500"
                        )}
                        title={
                          detectedPlatform === "unknown"
                            ? "Unsupported domain — the backend will try a generic extractor; results may vary."
                            : `Detected: ${PLATFORM_LABELS[detectedPlatform]}`
                        }
                      >
                        {PLATFORM_LABELS[detectedPlatform]}
                      </Badge>
                    )}
                  </div>
                </div>
              ) : (
                <div className="space-y-2">
                  <div className="flex gap-2">
                    <Select
                      size="sm"
                      className="w-32"
                      value={searchNetwork}
                      onChange={(e) => setSearchNetwork(e.target.value)}
                      disabled={networksLoading || networkOptions.length === 0}
                    >
                      {networkOptions.map((n) => (
                        <option key={n.value} value={n.value}>{n.label}</option>
                      ))}
                    </Select>
                    <Select
                      size="sm"
                      className="w-36"
                      value={searchStrategy}
                      onChange={(e) => setSearchStrategy(e.target.value)}
                    >
                      {strategyOptions.map((s) => (
                        <option key={s.value} value={s.value}>{s.label}</option>
                      ))}
                    </Select>
                  </div>
                  <div className="flex gap-2">
                    <Input
                      placeholder="Search by keyword or topic…"
                      value={searchQuery}
                      onChange={(e) => setSearchQuery(e.target.value)}
                      onKeyDown={(e) => e.key === "Enter" && void findBestMatch()}
                      className="h-8 text-xs flex-1"
                    />
                    <Button
                      size="sm"
                      onClick={() => void findBestMatch()}
                      disabled={isLoading || !searchQuery.trim()}
                      className="h-8 px-3 text-xs"
                    >
                      {isLoading ? <RefreshCw className="h-3 w-3 animate-spin" /> : <Search className="h-3 w-3" />}
                    </Button>
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          {/* Video card */}
          {videoResult && (
            <Card className="border-border/40 bg-background/60 overflow-hidden">
              <div className="relative aspect-video w-full overflow-hidden bg-muted/30">
                <img
                  src={videoResult.thumbnailUrl}
                  alt={videoResult.title}
                  className="w-full h-full object-cover"
                />
                <div className="absolute top-2 right-2">
                  <Badge variant="outline" className="bg-background/80 text-xs capitalize">
                    {videoResult.networkSource}
                  </Badge>
                </div>
                {videoResult.strategyMode && (
                  <div className="absolute top-2 left-2">
                    <Badge variant={strategyBadgeVariant(videoResult.strategyMode)} className="text-xs capitalize">
                      {videoResult.strategyMode}
                    </Badge>
                  </div>
                )}
              </div>
              <CardContent className="p-4 space-y-3">
                <div>
                  <h3 className="text-sm font-semibold text-foreground line-clamp-2">{videoResult.title}</h3>
                  <p className="mt-0.5 text-xs text-muted-foreground">{videoResult.author}</p>
                  <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
                    <Badge variant="outline" className="text-xs capitalize">
                      {videoResult.contentType ?? "video"}
                    </Badge>
                    {videoResult.publishedAtUtc && (
                      <Badge variant="outline" className="text-xs">
                        {formatDate(videoResult.publishedAtUtc)}
                      </Badge>
                    )}
                    {videoResult.dataSource === "apify" && (
                      <Badge variant="accent" className="text-xs">
                        Live data
                      </Badge>
                    )}
                  </div>
                </div>
                <div className="grid grid-cols-3 gap-2">
                  <Fact label="Views" value={videoResult.views.toLocaleString()} />
                  <Fact label="Likes" value={videoResult.likes.toLocaleString()} />
                  <Fact label="Comments" value={videoResult.comments.toLocaleString()} />
                  <Fact label="Shares" value={(videoResult.shares ?? 0).toLocaleString()} />
                  <Fact label="Saves" value={(videoResult.saves ?? 0).toLocaleString()} />
                  <Fact label="Duration" value={formatDuration(videoResult.durationSeconds)} />
                </div>
                {videoResult.hashtags && videoResult.hashtags.length > 0 && (
                  <div className="flex flex-wrap gap-1">
                    {videoResult.hashtags.slice(0, 8).map((tag) => (
                      <span key={tag} className="text-xs text-muted-foreground/70">
                        #{tag}
                      </span>
                    ))}
                  </div>
                )}
                {error && videoResult ? (
                  // Partial success: extraction worked, AI failed. Retry ONLY the AI stage using
                  // the persisted snapshot — no Apify call, no extra extraction tokens.
                  <Button
                    variant="outline"
                    className="w-full h-8 text-xs mt-1"
                    onClick={() => void retryAiAnalysis(videoResult.id)}
                    disabled={isLoading}
                  >
                    <RefreshCw className="h-3 w-3" />
                    Retry AI analysis
                  </Button>
                ) : !isAnalyzed ? (
                  <Button
                    className="w-full h-8 text-xs mt-1"
                    onClick={() => void runAnalysis()}
                    disabled={isAnalyzing}
                  >
                    {isAnalyzing ? (
                      <span className="flex items-center gap-2">
                        <RefreshCw className="h-3 w-3 animate-spin" />
                        Analyzing… {analyzingProgress}%
                      </span>
                    ) : (
                      "Analyze Video"
                    )}
                  </Button>
                ) : (
                  <div className="flex items-center gap-2 rounded-md bg-green-500/10 border border-green-500/30 px-3 py-2 text-xs text-green-600 dark:text-green-300">
                    <CheckCircle2 className="h-3.5 w-3.5" />
                    Analysis complete
                  </div>
                )}
                {isAnalyzing && <InlineProgress value={analyzingProgress} />}
              </CardContent>
            </Card>
          )}

          {/* Analysis detail — collapsible. */}
          {isAnalyzed && videoResult?.videoAnalysis && (
            <Card className="border-border/40 bg-background/60">
              <CardHeader className="pb-2">
                <button
                  type="button"
                  onClick={() => setIsVideoAnalysisCollapsed((v) => !v)}
                  className="w-full flex items-center justify-between gap-2 text-left"
                  aria-expanded={!isVideoAnalysisCollapsed}
                  aria-controls="video-analysis-body"
                >
                  <CardTitle className="text-sm flex items-center gap-2">
                    <ChevronRight
                      className={cn(
                        "h-4 w-4 text-purple-400 transition-transform",
                        !isVideoAnalysisCollapsed && "rotate-90"
                      )}
                    />
                    Video Analysis
                  </CardTitle>
                  <span className="text-xs text-muted-foreground/60">
                    {isVideoAnalysisCollapsed ? "Show" : "Hide"}
                  </span>
                </button>
              </CardHeader>
              {!isVideoAnalysisCollapsed && (
                <CardContent id="video-analysis-body" className="pt-0">
                  <pre className="whitespace-pre-wrap text-xs text-muted-foreground font-mono leading-relaxed">
                    {videoResult.videoAnalysis}
                  </pre>
                </CardContent>
              )}
            </Card>
          )}

          {/* Download */}
          {isAnalyzed && videoResult && (
            <Card className="border-border/40 bg-background/60">
              <CardHeader className="pb-2">
                <CardTitle className="text-sm flex items-center gap-2">
                  <Download className="h-4 w-4" />
                  Download
                </CardTitle>
              </CardHeader>
              <CardContent className="pt-0 space-y-3">
                {downloadResult ? (
                  <div className="space-y-2">
                    <div className="flex items-center gap-2 rounded-md bg-green-500/10 border border-green-500/30 px-3 py-2 text-xs text-green-600 dark:text-green-300">
                      <CheckCircle2 className="h-3.5 w-3.5" />
                      Ready: {downloadResult.fileName}
                    </div>
                    <Button
                      variant="outline"
                      size="sm"
                      className="w-full h-7 text-xs"
                      onClick={() => window.open(downloadResult.downloadUrl, "_blank")}
                    >
                      Open file
                    </Button>
                  </div>
                ) : (
                  <>
                    {isDownloading && <InlineProgress value={downloadProgress} />}
                    <Button
                      size="sm"
                      className="w-full h-8 text-xs"
                      onClick={() => void handleDownload()}
                      disabled={isDownloading}
                    >
                      {isDownloading ? `Preparing… ${downloadProgress}%` : "Download video + analysis"}
                    </Button>
                  </>
                )}
              </CardContent>
            </Card>
          )}
        </div>

        {/* ── RIGHT PANEL ── */}
        <div className="flex flex-col gap-4">
          {isAnalyzed ? (
            <Card className="border-border/40 bg-background/60 flex-1">
              <CardHeader className="pb-3">
                <TabBar
                  tabs={[
                    { key: "conversion", label: "Convert" },
                    { key: "preview", label: "Preview" },
                  ]}
                  active={rightPanelTab}
                  onChange={(k) => setRightPanelTab(k as "conversion" | "preview")}
                />
              </CardHeader>
              <CardContent className="pt-0">
                {rightPanelTab === "conversion" && (
                  <div className="space-y-4">
                    {/* Client context — chosen before generating, drives brand orientation */}
                    <div className="grid gap-1.5">
                      <label htmlFor="single-analyzer-client" className="text-xs font-medium text-muted-foreground">
                        Client context
                      </label>
                      <Select
                        id="single-analyzer-client"
                        size="sm"
                        className="w-full"
                        value={selectedClientId}
                        onChange={(e) => setSelectedClientId(e.target.value)}
                        disabled={clientsLoading}
                      >
                        <option value="">No client - neutral generation</option>
                        {clients.map((client) => (
                          <option key={client.id} value={client.id}>
                            {client.name}
                          </option>
                        ))}
                      </Select>
                      <p className="text-xs text-muted-foreground/70">
                        {selectedClient
                          ? `Content will be generated for ${selectedClient.name}.`
                          : "Pick a client to orient the generated content toward their brand."}
                      </p>
                    </div>

                    {/* Backend package pipeline: enqueue all formats, poll live status, then download zip. */}
                    <div className="space-y-2">
                      <div className="rounded-lg border border-purple-500/20 bg-purple-500/5 p-3 space-y-2">
                        <div className="flex items-start gap-2">
                          <Sparkles className="h-4 w-4 text-purple-400 mt-0.5 shrink-0" />
                          <div className="flex-1 min-w-0">
                            <p className="text-xs font-medium text-foreground">Convert to all formats</p>
                            <p className="text-xs text-muted-foreground/70">
                              Generate every supported format (carousel, flyer, reel-script, content idea,
                              CTA, caption, hook). Download the <code className="text-[10px]">package.zip</code> when ready.
                            </p>
                          </div>
                        </div>
                        <div className="flex gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            className="flex-1 h-8 text-xs"
                            onClick={() => void handleConvertAll()}
                            disabled={
                              packageGenerationIsRunning ||
                              packageGenerationIsCompleted ||
                              networkOptions.length === 0
                            }
                            title={
                              packageGenerationIsCompleted
                                ? "This analysis already finished all formats."
                                : undefined
                            }
                          >
                            {packageGenerationIsRunning ? (
                              <span className="flex items-center gap-2">
                                <RefreshCw className="h-3 w-3 animate-spin" />
                                Generating all formats…
                              </span>
                            ) : packageGenerationIsCompleted ? (
                              <span className="flex items-center gap-2">
                                <CheckCircle2 className="h-3 w-3 text-green-500" />
                                All formats generated
                              </span>
                            ) : packageGenerationIsPartial ? (
                              <span className="flex items-center gap-2">
                                <CheckCircle2 className="h-3 w-3 text-amber-500" />
                                Partial — retry?
                              </span>
                            ) : packageGenerationIsFailed ? (
                              <span className="flex items-center gap-2">
                                <XCircle className="h-3 w-3 text-red-500" />
                                Retry generation
                              </span>
                            ) : (
                              <span className="flex items-center gap-2">
                                <Sparkles className="h-3 w-3" />
                                Convert to all formats
                              </span>
                            )}
                          </Button>
                          <Button
                            size="sm"
                            variant="outline"
                            className="h-8 text-xs"
                            disabled={!packageDownloadAvailable || isDownloadingPackage}
                            onClick={() => void handlePackageDownload()}
                            title={packageDownloadAvailable ? "Download the generated package" : "Generate a package first"}
                          >
                            {isDownloadingPackage ? (
                              <RefreshCw className="h-3 w-3 animate-spin" />
                            ) : (
                              <Package className="h-3 w-3" />
                            )}
                            {isDownloadingPackage ? "Downloading..." : "package.zip"}
                          </Button>
                        </div>
                        {Object.keys(packageJob.perFormatStatus).length > 0 && (packageGenerationIsRunning || packageGenerationIsPartial || packageGenerationIsCompleted) && (
                          <div className="flex flex-wrap gap-1 pt-1">
                            {(Object.entries(packageJob.perFormatStatus) as [ContentFormatKey, string][]).map(([fmt, st]) => (
                              <span
                                key={fmt}
                                className={cn(
                                  "inline-flex items-center rounded px-1.5 py-0.5 text-[10px] border",
                                  st === "Rendered" || st === "Succeeded"
                                    ? "bg-green-500/10 text-green-600 dark:text-green-400 border-green-500/20"
                                    : st === "Failed"
                                    ? "bg-red-500/10 text-red-600 dark:text-red-400 border-red-500/20"
                                    : "bg-muted/40 text-muted-foreground border-border/30"
                                )}
                              >
                                {FORMAT_LABELS[fmt]} · {st.toLowerCase()}
                              </span>
                            ))}
                          </div>
                        )}
                      </div>

                      {packageJobError && (
                        <div className="rounded-md border border-red-500/30 bg-red-500/5 p-2 text-xs text-red-600 dark:text-red-400 flex items-start gap-2">
                          <XCircle className="h-3.5 w-3.5 mt-0.5 shrink-0" />
                          <span>{packageJobError.message}</span>
                        </div>
                      )}

                      {packageGenerationIsFailed && !packageJobError && (
                        <div className="rounded-md border border-red-500/30 bg-red-500/5 p-2 text-xs text-red-600 dark:text-red-400 flex items-start gap-2">
                          <XCircle className="h-3.5 w-3.5 mt-0.5 shrink-0" />
                          <span>Generation failed. You can retry above.</span>
                        </div>
                      )}

                      {packageGenerationIsPartial && (
                        <PartialCompletionBanner
                          succeededFormats={packageSucceededFormats}
                          failedFormats={packageFailedFormats}
                        />
                      )}

                      {(packageGenerationIsCompleted || packageGenerationIsPartial) &&
                        (packageJob.manifest?.inferredFields?.length ?? 0) > 0 && (
                          <InferredFieldsDisclosure fields={packageJob.manifest!.inferredFields!} />
                        )}
                    </div>

                    {/* Format mode selector */}
                    <div className="flex gap-1.5">
                      {(["script", "carousel", "flyer"] as const).map((mode) => (
                        <button
                          key={mode}
                          type="button"
                          onClick={() => setMutationMode(mode)}
                          className={cn(
                            "flex items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-medium border transition-colors capitalize",
                            mutationMode === mode
                              ? "border-purple-500/50 bg-purple-500/10 text-purple-400"
                              : "border-border/40 text-muted-foreground hover:text-foreground"
                          )}
                        >
                          {mode === "script" && <Film className="h-3 w-3" />}
                          {mode === "carousel" && <ImageIcon className="h-3 w-3" />}
                          {mode === "flyer" && <Megaphone className="h-3 w-3" />}
                          {mode}
                        </button>
                      ))}
                    </div>

                    {/* Per-network tabs */}
                    <div>
                      <TabBar
                        tabs={networkOptions.map((n) => ({ key: n.value, label: n.label }))}
                        active={previewNetworkTab}
                        onChange={setPreviewNetworkTab}
                      />
                      <div className="mt-3 space-y-3">
                        {activeContent(previewNetworkTab) ? (
                          <div className="rounded-lg border border-border/40 bg-muted/20 p-3">
                            <pre className="whitespace-pre-wrap text-xs text-foreground/80 font-mono leading-relaxed">
                              {activeContent(previewNetworkTab)}
                            </pre>
                          </div>
                        ) : (
                          <Button
                            size="sm"
                            variant="outline"
                            className="w-full h-8 text-xs"
                            onClick={() => void handleGenerate(previewNetworkTab)}
                            disabled={generatingConversion !== null}
                          >
                            {generatingConversion === `${previewNetworkTab}-${mutationMode}` ? (
                              <span className="flex items-center gap-2">
                                <RefreshCw className="h-3 w-3 animate-spin" />
                                Generating…
                              </span>
                            ) : (
                              `Generate ${mutationMode} for ${networkOptions.find((n) => n.value === previewNetworkTab)?.label ?? previewNetworkTab}`
                            )}
                          </Button>
                        )}
                      </div>
                    </div>
                  </div>
                )}

                {rightPanelTab === "preview" && (
                  <div>
                    <TabBar
                      tabs={networkOptions.map((n) => ({ key: n.value, label: n.label }))}
                      active={previewNetworkTab}
                      onChange={setPreviewNetworkTab}
                    />
                    <div className="mt-3">
                      {activeContent(previewNetworkTab) ? (
                        <SocialTabletPreview
                          content={activeContent(previewNetworkTab) ?? ""}
                          network={previewNetworkTab}
                        />
                      ) : (
                        <div className="flex flex-col items-center justify-center min-h-[160px] text-center text-xs text-muted-foreground/60 space-y-2">
                          <ImageIcon className="h-8 w-8 opacity-30" />
                          <p>Generate content in Convert tab to preview it here.</p>
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>
          ) : (
            <div className="flex flex-col items-center justify-center rounded-xl border border-border/30 border-dashed min-h-[300px] text-center p-8 space-y-3">
              <ScanSearch className="h-10 w-10 text-muted-foreground/30" />
              <p className="text-sm text-muted-foreground/60">
                Load a video and run analysis to unlock content generation.
              </p>
            </div>
          )}
        </div>
      </div>

      {/* History — saved analyses owned by the current user. Click to reload everything as if
          the run just finished (snapshot + analysis + any existing content-generation job). */}
      {historyItems.length > 0 && (
        <Card className="border-border/40 bg-background/60">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm flex items-center gap-2">
              <Clock className="h-4 w-4" />
              Saved Analyses
            </CardTitle>
          </CardHeader>
          <CardContent className="pt-0">
            <div className="divide-y divide-border/30">
              {historyItems.map((item) => (
                <SavedAnalysisRow
                  key={item.id}
                  item={item}
                  isActive={videoResult?.id === item.id}
                  isDeleting={deletingHistoryId === item.id}
                  onOpen={() => void handleLoadFromHistory(item)}
                  onDownload={() =>
                    item.latestContentGeneration?.jobId &&
                    void handleHistoryDownload(item.latestContentGeneration.jobId)
                  }
                  onDelete={() => void handleDeleteFromHistory(item)}
                />
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      <ConfirmationDialog
        open={isAbandonDialogOpen}
        title="Leave this page?"
        description="You have analysis content on screen that has not been saved or downloaded yet. If you leave now, this working context will be discarded."
        cancelLabel="Stay here"
        confirmLabel="Leave without saving"
        confirmVariant="destructive"
        onCancel={closeAbandonDialog}
        onConfirm={confirmAbandonNavigation}
      />
    </div>
  );
}

// ── History row ────────────────────────────────────────────────────────────────

function statusBadgeVariant(status: SingleContentAnalysisStatus): BadgeVariant {
  switch (status) {
    case "Analyzed":
      return "success";
    case "ContentResolved":
    case "ContentResolvedPartial":
      return "info";
    case "Failed":
      return "destructive";
    case "Requested":
    case "ResolvingContent":
    case "Analyzing":
      return "warning";
    default:
      return "muted";
  }
}

function statusLabel(status: SingleContentAnalysisStatus): string {
  switch (status) {
    case "Requested":
      return "Queued";
    case "ResolvingContent":
      return "Scraping";
    case "ContentResolved":
      return "Scraped";
    case "ContentResolvedPartial":
      return "Scraped (partial)";
    case "Analyzing":
      return "Analyzing";
    case "Analyzed":
      return "Analyzed";
    case "Failed":
      return "Failed";
    default:
      return status;
  }
}

function generationBadge(status: string): { label: string; variant: BadgeVariant } {
  switch (status) {
    case "Completed":
      return { label: "Generated", variant: "success" };
    case "PartialSuccess":
    case "Partial":
      return { label: "Partial", variant: "warning" };
    case "Failed":
      return { label: "Generation failed", variant: "destructive" };
    case "Running":
      return { label: "Generating…", variant: "info" };
    case "Pending":
      return { label: "Queued", variant: "muted" };
    default:
      return { label: status, variant: "muted" };
  }
}

interface SavedAnalysisRowProps {
  item: SingleContentAnalysisHistoryItem;
  isActive: boolean;
  isDeleting: boolean;
  onOpen: () => void;
  onDownload: () => void;
  onDelete: () => void;
}

function SavedAnalysisRow({ item, isActive, isDeleting, onOpen, onDownload, onDelete }: SavedAnalysisRowProps) {
  const gen = item.latestContentGeneration;
  const canDownload =
    gen?.hasPackage === true &&
    (gen.status === "Completed" || gen.status === "Partial" || gen.status === "PartialSuccess");

  return (
    <div
      className={cn(
        "flex items-center gap-3 py-2.5 px-1 -mx-1 rounded-md transition-colors",
        isActive && "bg-muted/40"
      )}
    >
      <button
        type="button"
        onClick={onOpen}
        className="flex-1 min-w-0 text-left hover:opacity-90 focus:outline-none focus-visible:ring-1 focus-visible:ring-ring rounded"
        title="Open this saved analysis"
      >
        <p className="text-xs font-medium text-foreground/90 truncate">
          {item.normalizedUrl ?? item.sourceUrl}
        </p>
        <p className="text-xs text-muted-foreground/60 capitalize">
          {item.sourcePlatformKey ?? "unknown"} · {formatDate(item.createdAtUtc)}
        </p>
      </button>

      <Badge variant={statusBadgeVariant(item.status)} className="text-xs shrink-0">
        {statusLabel(item.status)}
      </Badge>

      {gen && (() => {
        const b = generationBadge(gen.status);
        return (
          <Badge variant={b.variant} className="text-xs shrink-0">
            {b.label}
          </Badge>
        );
      })()}

      {canDownload && (
        <Button
          size="sm"
          variant="ghost"
          className="h-7 px-2"
          onClick={onDownload}
          title="Re-download the generated package"
        >
          <Download className="h-3.5 w-3.5" />
        </Button>
      )}

      <Button
        size="sm"
        variant="ghost"
        className="h-7 px-2 text-muted-foreground hover:text-destructive"
        onClick={onDelete}
        disabled={isDeleting}
        title="Delete this analysis (hard delete)"
      >
        {isDeleting ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}
      </Button>
    </div>
  );
}
