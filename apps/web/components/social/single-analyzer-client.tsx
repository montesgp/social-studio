"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode, SyntheticEvent } from "react";
import { useRouter } from "next/navigation";
import {
  ScanSearch,
  Search,
  Download,
  Clock,
  RefreshCw,
  Building2,
  Film,
  ImageIcon,
  Megaphone,
  CheckCircle2,
  ChevronRight,
  Sparkles,
  Package,
  Trash2,
  XCircle,
  AlertTriangle,
  FileText,
  FolderOpen,
  FileImage,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { BlockingLoader } from "@/components/ui/blocking-loader";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog";
import { Input } from "@/components/ui/input";
import { OptionCardSelector, type OptionCardSelectorOption } from "@/components/ui/option-card-selector";
import { Select } from "@/components/ui/select";
import { getMockClients, getMockOrganizations } from "@/lib/mocks/content-workflow";
import { listWorkflowClients } from "@/lib/api/content-workflow";
import type {
  Organization,
  SingleContentAnalysisStatus,
  SingleContentAnalysisHistoryItem,
  SocialClient,
  ContentFormatKey,
  ContentGenerationJobStatus,
  GeneratedPackageManifest,
} from "@/lib/api/types";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { useSocialNetworks } from "@/hooks/use-social-networks";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useToast } from "@/components/ui/toaster";
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
  findSingleContentAnalysisByUrl,
  mapBackendResponseToResult,
  retrySingleContentAnalysisAi,
} from "@/lib/api/single-analyzer";
import {
  requestPackage,
  downloadPackageFile,
  assemblePackage,
  deleteFormatItem,
  ALL_FORMAT_KEYS,
  FORMAT_LABELS,
} from "@/lib/api/content-generation";
import { useContentGenerationJob } from "@/lib/hooks/use-content-generation-job";
import { usePackageWorkspace } from "@/lib/hooks/use-package-workspace";
import { InferredFieldsDisclosure } from "@/components/social/single-analyzer/inferred-fields-disclosure";
import { PartialCompletionBanner } from "@/components/social/single-analyzer/partial-completion-banner";
import { SupportError } from "@/components/support/support-error";
import { openWhatsAppSupport } from "@/lib/support/support";
import { AnalysisOverlay } from "@/components/social/analysis-overlay";
import { AnalyzerToolsToolbar } from "@/components/social/analyzer-tools-toolbar";

// ── Async/poll constants ───────────────────────────────────────────────────────

const POLL_INTERVAL_MS = 2500;
const POLL_TIMEOUT_MS = 5 * 60 * 1000; // 5 min hard cap so the overlay can't hang forever
const FALLBACK_THUMBNAIL_PATH = "/images/thumbnail-unavailable.svg";
const MAX_REGENERATION_OBJECTION_LENGTH = 1000;

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
      label: "Requested formats generated",
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

// ── Per-format local tracking ──────────────────────────────────────────────────

type LocalFormatEntry = {
  content: string;
  generatedAt: string; // ISO timestamp — when the generation job was requested
  regenerationCount: number; // 0 = first generation, ≥1 = regenerated N times
};

const SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS: ContentFormatKey[] = [
  "reel-script",
  "carousel",
  "flyer",
];

const LOCAL_NO_CLIENT_KEY = "__no_client__";

function localClientKey(clientId: string | null | undefined): string {
  const normalized = clientId?.trim();
  return normalized && normalized.length > 0 ? normalized : LOCAL_NO_CLIENT_KEY;
}

function scopedFormatKey(clientId: string | null | undefined, formatKey: ContentFormatKey): string {
  return `${localClientKey(clientId)}:${formatKey}`;
}

function isKnownFormatKey(key: string): key is ContentFormatKey {
  return (ALL_FORMAT_KEYS as string[]).includes(key);
}

/** Default filenames shown in the Package tab tree when the backend manifest is absent. */
const FORMAT_FILE_NAMES: Record<ContentFormatKey, string> = {
  "reel-script": "reel-script.txt",
  flyer: "flyer.png",
  carousel: "carousel/",
  "content-idea": "content-idea.txt",
  "cta-variation": "cta-variation.txt",
  caption: "caption.txt",
  hook: "hook.txt",
};

/**
 * Resolves the display filename for a format. Uses the manifest filename when available,
 * but corrects the extension if the backend returns a wrong one (e.g. reel-script.png → .txt).
 * Folder formats always use the canonical "/" representation.
 */
function resolveFormatFilename(formatKey: ContentFormatKey, manifestFilename?: string): string {
  const canonical = FORMAT_FILE_NAMES[formatKey];
  if (!manifestFilename) return canonical;
  if (canonical.endsWith("/")) return canonical; // folder format — always canonical
  const dotIdx = canonical.lastIndexOf(".");
  if (dotIdx === -1) return manifestFilename;
  const expectedExt = canonical.slice(dotIdx);
  if (!manifestFilename.endsWith(expectedExt)) {
    const baseDot = manifestFilename.lastIndexOf(".");
    const base = baseDot !== -1 ? manifestFilename.slice(0, baseDot) : manifestFilename;
    return base + expectedExt;
  }
  return manifestFilename;
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

// ── Client folder slug derivation (mirrors backend NormalizeFolderName) ────────

/**
 * Derives a URL-safe client folder slug from a client name, using the same
 * normalization rules as the backend GeneratedPackageBuilder.NormalizeFolderName:
 * lowercase → replace runs of non-alphanumeric chars with a single hyphen → trim hyphens.
 *
 * Examples:
 *   "Acme Corp"   → "acme-corp"
 *   "Café & Co"   → "caf-co"
 *   "Test Client" → "test-client"
 */
export function deriveClientFolderSlug(name: string): string {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function RegenerationChoicePanel({
  onRegenerate,
  onWithObjections,
  disabled = false,
}: {
  onRegenerate: () => void;
  onWithObjections: () => void;
  disabled?: boolean;
}) {
  return (
    <div className="flex flex-wrap gap-2 rounded-md border border-border/40 bg-muted/20 p-2">
      <Button size="sm" variant="outline" className="h-8 text-xs" onClick={onRegenerate} disabled={disabled}>
        <RefreshCw className="h-3 w-3" />
        Regenerate
      </Button>
      <Button size="sm" variant="outline" className="h-8 text-xs" onClick={onWithObjections} disabled={disabled}>
        <AlertTriangle className="h-3 w-3" />
        With objections
      </Button>
    </div>
  );
}

function RegenerationObjectionEditor({
  value,
  onChange,
  onCancel,
  onSubmit,
  disabled,
  submitLabel,
}: {
  value: string;
  onChange: (value: string) => void;
  onCancel: () => void;
  onSubmit: () => void;
  disabled: boolean;
  submitLabel: string;
}) {
  const trimmed = value.trim();

  return (
    <div className="space-y-2 rounded-md border border-amber-500/30 bg-amber-500/5 p-3">
      <label htmlFor="regeneration-objection" className="text-xs font-medium text-foreground/90">
        Objections for regeneration
      </label>
      <textarea
        id="regeneration-objection"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        maxLength={MAX_REGENERATION_OBJECTION_LENGTH}
        rows={3}
        placeholder="Describe what should be corrected in the generated content..."
        className="w-full resize-none rounded-md border border-input bg-background px-3 py-2 text-xs ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      />
      <div className="flex items-center justify-between gap-2">
        <span className="text-[10px] text-muted-foreground/70">
          {value.length}/{MAX_REGENERATION_OBJECTION_LENGTH}
        </span>
        <div className="flex gap-2">
          <Button size="sm" variant="ghost" className="h-8 text-xs" onClick={onCancel}>
            <XCircle className="h-3 w-3" />
            Cancel
          </Button>
          <Button
            size="sm"
            variant="outline"
            className="h-8 text-xs"
            onClick={onSubmit}
            disabled={disabled || trimmed.length === 0}
          >
            <AlertTriangle className="h-3 w-3" />
            {submitLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

interface SingleAnalyzerClientProps {
  isPlatformAdmin: boolean;
}

type MutationMode = "script" | "carousel" | "flyer";

type PendingRegenerationRequest =
  | {
      kind: "single";
      mode: MutationMode;
      formatKey: ContentFormatKey;
      existingFormats: ContentFormatKey[];
      regenerationObjection: string | null;
    }
  | {
      kind: "all";
      formats: ContentFormatKey[];
      existingFormats: ContentFormatKey[];
      regenerationObjection: string | null;
    };

type RegenerationTarget =
  | { kind: "single"; mode: MutationMode; formatKey: ContentFormatKey }
  | { kind: "all" };

function isSameRegenerationTarget(
  left: RegenerationTarget | null,
  right: RegenerationTarget
): boolean {
  if (!left || left.kind !== right.kind) return false;
  if (left.kind === "all" || right.kind === "all") return true;
  return left.mode === right.mode && left.formatKey === right.formatKey;
}

export function SingleAnalyzerClient({ isPlatformAdmin }: SingleAnalyzerClientProps) {
  const router = useRouter();
  const toast = useToast();
  const { orgId: activeOrgId } = useActiveOrg();
  const token = useAccessToken();
  const effectiveToken = token ?? undefined;
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState("");
  const [clients, setClients] = useState<SocialClient[]>([]);
  const [selectedClientId, setSelectedClientId] = useState("");
  const [clientsLoading, setClientsLoading] = useState(false);
  const { networks: socialNetworks, loading: networksLoading } = useSocialNetworks(effectiveToken);
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
  const [localFormatsByClient, setLocalFormatsByClient] = useState<
    Record<string, Partial<Record<ContentFormatKey, LocalFormatEntry>>>
  >({});
  const [generatingConversion, setGeneratingConversion] = useState<MutationMode | null>(null);
  const [rightPanelTab, setRightPanelTab] = useState<"conversion" | "preview" | "package">("conversion");
  const [mutationMode, setMutationMode] = useState<MutationMode>("script");
  const [historyItems, setHistoryItems] = useState<SingleContentAnalysisHistoryItem[]>([]);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [deletingHistoryId, setDeletingHistoryId] = useState<string | null>(null);
  const [pendingDeleteHistoryItem, setPendingDeleteHistoryItem] =
    useState<SingleContentAnalysisHistoryItem | null>(null);
  const [duplicateAnalysisPrompt, setDuplicateAnalysisPrompt] =
    useState<SingleContentAnalysisHistoryItem | null>(null);
  const [isCheckingDuplicate, setIsCheckingDuplicate] = useState(false);
  const [pendingRegeneration, setPendingRegeneration] =
    useState<PendingRegenerationRequest | null>(null);
  // When loading from history, we want the new analysis's existing content-generation job to
  // re-attach to the convert panel. The packageJobId-reset effect runs AFTER setVideoResult,
  // so we pre-stage both the jobId and client context here and the effect reads them as it runs.
  const pendingPackageReattachRef = useRef<{ jobId: string | null; clientId: string | null }>({
    jobId: null,
    clientId: null,
  });
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

  // Formats that were physically deleted in this session (INC-5).
  // Tracked locally so the Convert button reverts to "Generate" without a page refresh.
  const [deletedFormatKeys, setDeletedFormatKeys] = useState<Set<string>>(new Set());
  // True while a regenerate or delete-format operation is in progress.
  const [isFormatMutating, setIsFormatMutating] = useState(false);

  // Content-generation package job state.
  const [packageJobId, setPackageJobId] = useState<string | null>(null);
  const [packageRequestedFormats, setPackageRequestedFormats] = useState<ContentFormatKey[]>(
    SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS
  );
  const [packageJobError, setPackageJobError] = useState<{ message: string } | null>(null);
  const [isRequestingPackage, setIsRequestingPackage] = useState(false);
  // True while we are reattaching a pre-existing job loaded from history (initial status check).
  // Prevents PackageGenerationOverlay from flashing for an already-completed job.
  const [isReattachingJob, setIsReattachingJob] = useState(false);
  // The client the current packageJobId was generated for (null = unknown or no job yet).
  const [packageJobClientId, setPackageJobClientId] = useState<string | null>(null);

  // UX state for the new agregados.
  const [isVideoAnalysisCollapsed, setIsVideoAnalysisCollapsed] = useState(false);
  const [isDescriptionExpanded, setIsDescriptionExpanded] = useState(false);
  const [regenerationObjection, setRegenerationObjection] = useState("");
  const [regenerationChoiceTarget, setRegenerationChoiceTarget] =
    useState<RegenerationTarget | null>(null);
  const [regenerationObjectionTarget, setRegenerationObjectionTarget] =
    useState<RegenerationTarget | null>(null);
  const [isSavedAnalysisView, setIsSavedAnalysisView] = useState(false);

  // Live-detected network from the URL the user is typing (frontend hint; backend is authoritative).
  const detectedPlatform = useMemo(() => detectPlatformFromUrl(urlInput), [urlInput]);
  const canAnalyzeByUrl =
    urlInput.trim().length > 0 &&
    detectedPlatform !== null &&
    detectedPlatform !== "unknown";

  // INC-6: Multi-client package workspace — hydrated from the DB, survives refresh.
  // clientPackages is keyed by clientId and contains the accumulated formats + assets per client.
  const {
    clientPackages,
    isLoadingPackages,
    hasStoredFormatsForClient,
    markFormatDeleted,
    refreshPackages,
  } = usePackageWorkspace(videoResult?.id, effectiveToken);
  const selectedClientLocalFormats =
    localFormatsByClient[localClientKey(selectedClientId)] ?? {};
  const hasAnyStoredGeneratedFormat = useMemo(
    () => Object.values(clientPackages).some((entry) => entry.formats.length > 0),
    [clientPackages]
  );

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
    (packageJob.isLoading && !isReattachingJob) ||
    packageJob.isPolling ||
    packageUiStatus === "Queued" ||
    packageUiStatus === "Running";
  const packageGenerationIsCompleted = packageUiStatus === "Completed";
  const packageGenerationIsAllFormatJob =
    packageRequestedFormats.length === SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS.length &&
    SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS.every((format) => packageRequestedFormats.includes(format));
  const allFormatsGenerationIsCompleted =
    packageGenerationIsCompleted && packageGenerationIsAllFormatJob;
  const packageGenerationIsPartial =
    packageUiStatus === "Partial" || packageUiStatus === "PartialSuccess";
  const packageGenerationIsFailed = packageUiStatus === "Failed";
  const canAssembleFromStoredAssets =
    Boolean(videoResult?.id) &&
    (hasAnyStoredGeneratedFormat || packageJob.manifest?.has_stored_assets === true);
  const packageDownloadAvailable =
    canAssembleFromStoredAssets ||
    ((packageGenerationIsCompleted || packageGenerationIsPartial) && packageJobId !== null);
  const hasUnsavedWork =
    isSavedAnalysisView &&
    regenerationObjectionTarget !== null &&
    regenerationObjection.trim().length > 0;
  const initialToolLoading =
    networksLoading ||
    historyLoading ||
    (selectedOrganizationId.length > 0 && clientsLoading && clients.length === 0);

  const currentPackageJobMatchesSelectedClient =
    selectedClientId.length > 0 && packageJobClientId === selectedClientId;

  const packageJobSucceededFormats = useMemo(
    () => {
      const fromPerFormatStatus = (Object.entries(packageJob.perFormatStatus)
        .filter(([, st]) => st === "Rendered" || st === "Succeeded")
        .map(([fmt]) => fmt)
        .filter(isKnownFormatKey));
      // Fallback for legacy jobs where perFormatStatus may be empty despite a terminal success:
      // use manifest.formats keys, guarded to known format keys and terminal-success statuses.
      const isTerminalSuccess =
        packageJob.status === "Completed" ||
        packageJob.status === "Partial" ||
        packageJob.status === "PartialSuccess";
      const fromManifest: ContentFormatKey[] =
        fromPerFormatStatus.length === 0 && isTerminalSuccess && packageJob.manifest?.formats
          ? Object.keys(packageJob.manifest.formats).filter(isKnownFormatKey)
          : [];
      return Array.from(new Set([...fromPerFormatStatus, ...fromManifest]));
    },
    [packageJob.perFormatStatus, packageJob.status, packageJob.manifest]
  );
  const selectedClientStoredFormatKeys = useMemo(
    () => {
      if (!selectedClientId) return [];
      const entry = clientPackages[selectedClientId];
      if (!entry) return [];
      return entry.formats
        .map((format) => format.formatKey)
        .filter(isKnownFormatKey);
    },
    [clientPackages, selectedClientId]
  );
  const packageSucceededFormats = useMemo(
    () => {
      const fromCurrentJob = currentPackageJobMatchesSelectedClient ? packageJobSucceededFormats : [];
      const all = Array.from(new Set([...selectedClientStoredFormatKeys, ...fromCurrentJob]));
      return all.filter((fmt) => !deletedFormatKeys.has(scopedFormatKey(selectedClientId, fmt)));
    },
    [
      currentPackageJobMatchesSelectedClient,
      deletedFormatKeys,
      packageJobSucceededFormats,
      selectedClientId,
      selectedClientStoredFormatKeys,
    ]
  );
  const packageFailedFormats = useMemo(
    () =>
      currentPackageJobMatchesSelectedClient
        ? (Object.entries(packageJob.perFormatStatus)
        .filter(([, st]) => st === "Failed")
        .map(([fmt]) => fmt)
        .filter(isKnownFormatKey))
        : [],
    [currentPackageJobMatchesSelectedClient, packageJob.perFormatStatus]
  );

  // INC-6: Refresh the multi-client packages tree whenever a generation job reaches a terminal state.
  // This ensures a newly-generated client folder appears in the Packages tab without a page refresh.
  useEffect(() => {
    const terminalStatuses = ["Completed", "Partial", "PartialSuccess"] as const;
    if (packageJob.status && (terminalStatuses as readonly string[]).includes(packageJob.status)) {
      refreshPackages();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [packageJob.status]);

  const downloadCurrentPackage = useCallback(async () => {
    if (isDownloadingPackage) return;
    const analysisId = videoResult?.id;
    const shouldAssemble = Boolean(analysisId) && canAssembleFromStoredAssets;
    if (!shouldAssemble && !packageJobId) return;
    setIsDownloadingPackage(true);
    setPackageJobError(null);
    try {
      // Dual-path download:
      // 1) Assemble from currently stored assets (preferred; survives cross-job regenerations).
      // 2) Fallback to legacy frozen-ZIP download for older jobs.

      let blob: Blob;
      let filename: string;

      if (shouldAssemble && analysisId) {
        ({ blob, filename } = await assemblePackage(analysisId, undefined, { token: effectiveToken }));
      } else {
        ({ blob, filename } = await downloadPackageFile(packageJobId!, { token: effectiveToken }));
      }

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
  }, [packageJobId, isDownloadingPackage, effectiveToken, videoResult?.id, canAssembleFromStoredAssets]);

  useEffect(() => {
    setDuplicateAnalysisPrompt(null);
  }, [urlInput]);

  useEffect(() => {
    setIsDescriptionExpanded(false);
  }, [videoResult?.id]);

  // Cancel any in-flight polling loop on unmount.
  useEffect(() => {
    return () => {
      pollCancelRef.current = true;
    };
  }, []);

  // Guard only local drafts that have not been sent to the API.
  useEffect(() => {
    if (!hasUnsavedWork) return;
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      // Modern browsers ignore the message and show their own; setting returnValue is required
      // to trigger the confirmation dialog at all.
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [hasUnsavedWork]);

  // Themed in-app abandonment guard for navigation clicks (sidebar/menu links).
  useEffect(() => {
    if (!hasUnsavedWork) return;

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
  }, [hasUnsavedWork]);

  // Derived — always in sync with API data
  const networkOptions = socialNetworks.map((n) => ({ value: n.code, label: n.displayName }));
  const selectedClient = useMemo(
    () => clients.find((client) => client.id === selectedClientId) ?? null,
    [clients, selectedClientId]
  );
  const clientContextOptions = useMemo<OptionCardSelectorOption[]>(
    () =>
      clients.map((client) => ({
        value: client.id,
        label: client.name,
        description: deriveClientFolderSlug(client.name) || client.id,
        icon: <Building2 className="h-4 w-4" />,
      })),
    [clients]
  );
  const hasClientContext = selectedClientId.trim().length > 0;
  const generationClientId = selectedClientId;
  const packageGenerationClientMissing = !generationClientId;
  // A saved analysis can be converted for multiple active clients; each generated output
  // is scoped by the client selected at generation time.

  // Normalize searchNetwork to first available network code when networks load.
  useEffect(() => {
    if (socialNetworks.length === 0) return;
    setSearchNetwork((prev) =>
      socialNetworks.some((n) => n.code === prev) ? prev : socialNetworks[0].code
    );
  }, [socialNetworks]);

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
    setLocalFormatsByClient({});
    setDeletedFormatKeys(new Set());
    setError(null);
    setPackageJobId(null);
    setPackageJobClientId(null);
    setPackageRequestedFormats(SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS);
    setPackageJobError(null);
    setIsDescriptionExpanded(false);
    setIsVideoAnalysisCollapsed(false);
    setRegenerationObjection("");
    setRegenerationChoiceTarget(null);
    setRegenerationObjectionTarget(null);
    setIsSavedAnalysisView(false);
    pendingPackageReattachRef.current = { jobId: null, clientId: null };
  }

  function startNewAnalysis() {
    resetVideoState();
    setInputMode("url");
    setUrlInput("");
    setSearchQuery("");
    setSelectedClientId("");
    setDuplicateAnalysisPrompt(null);
    setRightPanelTab("conversion");
    setMutationMode("script");
    setPendingRegeneration(null);
    setPendingDeleteHistoryItem(null);
    setPendingNavigationHref(null);
    setIsAbandonDialogOpen(false);
    pollCancelRef.current = false;
  }

  function saveCurrentWork() {
    setRegenerationObjection("");
    setRegenerationChoiceTarget(null);
    setRegenerationObjectionTarget(null);
    void loadHistory({ silent: true });
    toast.success("Work saved", "The current analysis and generated assets are persisted.");
  }

  function changeClientContext(clientId: string) {
    setSelectedClientId(clientId);
    if (!clientId) {
      setRegenerationObjection("");
      setRegenerationChoiceTarget(null);
      setRegenerationObjectionTarget(null);
      setPackageJobError(null);
    }
  }

  function openAnalyzerHelp() {
    openWhatsAppSupport({
      feature: "single-analyzer",
      organizationId: selectedOrganizationId,
      details: {
        analysisId: videoResult?.id ?? null,
        savedAnalysis: isSavedAnalysisView,
      },
    });
  }

  async function loadVideoByUrl(opts?: { force?: boolean }) {
    const trimmedUrl = urlInput.trim();
    if (!trimmedUrl) return;
    const platform = detectPlatformFromUrl(trimmedUrl);
    if (!platform || platform === "unknown") {
      setDuplicateAnalysisPrompt(null);
      setError({
        code: "social.single_analyzer.invalid_url_input",
        message:
          "We can only analyze valid social URLs from TikTok, Instagram, YouTube, X, Facebook, or LinkedIn.",
      });
      return;
    }

    if (!opts?.force && effectiveToken) {
      setIsCheckingDuplicate(true);
      setError(null);
      try {
        const duplicate = await findSingleContentAnalysisByUrl(trimmedUrl, effectiveToken);
        if (duplicate.item) {
          setDuplicateAnalysisPrompt(duplicate.item);
          return;
        }
      } catch {
        // Do not block the user's explicit analysis request on a lookup-only failure.
      } finally {
        setIsCheckingDuplicate(false);
      }
    }

    setDuplicateAnalysisPrompt(null);
    setIsLoading(true);
    resetVideoState();
    setIsSavedAnalysisView(false);
    setAnalysisStatus("Requested");
    pollCancelRef.current = false;
    try {
      // ASYNC/POLL: POST queues the work and returns immediately with the analysis id/status.
      // We then poll until Analyzed or Failed. The browser never contacts Apify directly
      // (CAP5-MUST-01/03) and the long extraction + AI run is decoupled from this request,
      // so a slow client connection never cancels the backend work.
      const started = await analyzeSingleContent(
        {
          url: trimmedUrl,
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
        }
        setError({
          code: final.failureCode ?? "social.single_analyzer.analysis_failed",
          message: final.failureMessageKey ?? "Analysis failed. Please try again.",
        });
        return;
      }

      const result = mapBackendResponseToResult(final, selectedOrganizationId);
      setVideoResult(result);
      setSelectedClientId(final.clientId ?? "");
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
      setSelectedClientId(final.clientId ?? "");
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
    setIsSavedAnalysisView(false);
    await sleep(700);
    try {
      const result = getMockVideoBySearch(
        selectedOrganizationId,
        searchQuery.trim(),
        searchNetwork,
        searchStrategy
      );
      setVideoResult(result);
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

  function formatForMutationMode(mode: MutationMode = mutationMode): ContentFormatKey {
    if (mode === "script") return "reel-script";
    if (mode === "carousel") return "carousel";
    return "flyer";
  }

  function hasLocalGeneratedFormat(formatKey: ContentFormatKey): boolean {
    // A locally-tracked format is client-scoped and is NOT considered generated if it was deleted in this session.
    return Boolean(selectedClientLocalFormats[formatKey]) &&
      !deletedFormatKeys.has(scopedFormatKey(selectedClientId, formatKey));
  }

  function getExistingGeneratedFormats(formats: ContentFormatKey[]): ContentFormatKey[] {
    return formats.filter(
      (format) => packageSucceededFormats.includes(format) || hasLocalGeneratedFormat(format)
    );
  }

  async function startSingleFormatGeneration(
    mode: MutationMode,
    regenerationObjection?: string | null
  ) {
    if (!videoResult) return;
    setGeneratingConversion(mode);
    const formatKey = formatForMutationMode(mode);
    setPackageJobError(null);
    try {
      const response = await requestPackage(
        videoResult.id,
        {
          clientId: selectedClientId || null,
          requestedFormats: [formatKey],
          regenerationObjection: regenerationObjection ?? null,
        },
        { token: effectiveToken }
      );
      setPackageRequestedFormats([formatKey]);
      setPackageJobId(response.jobId);
      setPackageJobClientId(selectedClientId || null);
      setLocalFormatsByClient((prev) => {
        const clientKey = localClientKey(selectedClientId);
        const existingFormats = prev[clientKey] ?? {};
        const existing = existingFormats[formatKey];
        return {
          ...prev,
          [clientKey]: {
            ...existingFormats,
            [formatKey]: {
              content: "",
              generatedAt: new Date().toISOString(),
              regenerationCount: existing ? existing.regenerationCount + 1 : 0,
            },
          },
        };
      });
      setDeletedFormatKeys((prev) => {
        const next = new Set(prev);
        next.delete(scopedFormatKey(selectedClientId, formatKey));
        return next;
      });
      if (regenerationObjection) {
        setRegenerationObjection("");
      }
      setRegenerationChoiceTarget(null);
      setRegenerationObjectionTarget(null);
    } catch (e) {
      const err = e as { message?: string };
      setPackageJobError({ message: err?.message ?? "Failed to start content generation." });
    } finally {
      setGeneratingConversion(null);
    }
  }

  /**
   * INC-5: Physically deletes a format's stored asset from the backend (storage + DB rows).
   * Reverts the Convert button to "Generate" and removes the format from the Packages tree.
   */
  async function handleDeleteFormat(formatKey: ContentFormatKey) {
    const analysisId = videoResult?.id;
    const clientId = packageJobClientId ?? selectedClientId;
    if (!analysisId || !clientId || isFormatMutating) return;
    setIsFormatMutating(true);
    setPackageJobError(null);
    try {
      await deleteFormatItem(analysisId, formatKey, clientId, { token: effectiveToken });
      // Track as deleted so the Convert button reverts to "Generate".
      setDeletedFormatKeys((prev) => new Set([...prev, scopedFormatKey(clientId, formatKey)]));
      // Remove from localFormats so it disappears from the Packages tree.
      setLocalFormatsByClient((prev) => {
        const clientKey = localClientKey(clientId);
        const existingFormats = prev[clientKey];
        if (!existingFormats) return prev;
        const nextFormats = { ...existingFormats };
        delete nextFormats[formatKey];
        const next = { ...prev };
        if (Object.keys(nextFormats).length === 0) {
          delete next[clientKey];
        } else {
          next[clientKey] = nextFormats;
        }
        return next;
      });
      // INC-6: Remove the format from the per-client package tree.
      markFormatDeleted(clientId, formatKey);
    } catch (e) {
      const err = e as { message?: string };
      setPackageJobError({ message: err?.message ?? `Failed to delete ${formatKey}.` });
    } finally {
      setIsFormatMutating(false);
    }
  }

  function resolveRegenerationObjection(includeObjection: boolean): string | null {
    if (!includeObjection) return null;
    const normalized = regenerationObjection.trim();
    if (normalized.length === 0) {
      setPackageJobError({
        message: "Add objection notes before using regenerate with objections.",
      });
      return null;
    }
    setPackageJobError(null);
    return normalized;
  }

  const generatedFormatKeys = SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS.filter(
    (format) => packageSucceededFormats.includes(format) || hasLocalGeneratedFormat(format)
  );
  const allActiveFormatsGenerated = SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS.every((format) =>
    generatedFormatKeys.includes(format)
  );
  const shouldRegenerateAllFormats = allFormatsGenerationIsCompleted || allActiveFormatsGenerated;
  const convertAllPills = SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS
    .filter((format) => {
      if (generatedFormatKeys.includes(format)) return true;
      return Boolean(currentPackageJobMatchesSelectedClient && packageJob.perFormatStatus[format]);
    })
    .map((format) => {
      const liveStatus = currentPackageJobMatchesSelectedClient
        ? packageJob.perFormatStatus[format]
        : null;
      const status = liveStatus ?? "Generated";
      return { format, status };
    });
  // INC-6: hasAnyGeneratedFormat also checks the persisted multi-client workspace.
  const hasAnyGeneratedFormat =
    generatedFormatKeys.length > 0 ||
    (selectedClientId ? hasStoredFormatsForClient(selectedClientId) : false) ||
    hasAnyStoredGeneratedFormat;
  const canDownloadPackage = hasAnyGeneratedFormat && packageDownloadAvailable;

  async function handlePackageAction() {
    if (isDownloadingPackage || isRequestingPackage || packageGenerationIsRunning) return;
    if (!canDownloadPackage) {
      setPackageJobError({
        message: "Package is not ready yet. Finish at least one successful generation first.",
      });
      return;
    }

    await downloadCurrentPackage();
  }

  function toggleRegenerationChoices(target: RegenerationTarget) {
    setPackageJobError(null);
    setRegenerationObjectionTarget(null);
    setRegenerationChoiceTarget((current) =>
      isSameRegenerationTarget(current, target) ? null : target
    );
  }

  function showRegenerationObjection(target: RegenerationTarget) {
    setPackageJobError(null);
    setRegenerationChoiceTarget(null);
    setRegenerationObjection("");
    setRegenerationObjectionTarget(target);
  }

  function startSimpleRegeneration(target: RegenerationTarget) {
    setRegenerationChoiceTarget(null);
    setRegenerationObjectionTarget(null);
    setRegenerationObjection("");

    if (target.kind === "all") {
      void handleConvertAll(false);
      return;
    }

    void handleGenerate(false, target.mode);
  }

  function submitRegenerationWithObjection(target: RegenerationTarget) {
    if (target.kind === "all") {
      void handleConvertAll(true);
      return;
    }

    void handleGenerate(true, target.mode);
  }

  async function handleGenerate(includeObjection = false, modeOverride?: MutationMode) {
    if (!videoResult) return;
    const mode = modeOverride ?? mutationMode;
    const formatKey = formatForMutationMode(mode);
    const currentObjection = resolveRegenerationObjection(includeObjection);
    if (includeObjection && !currentObjection) return;
    const existingFormats = getExistingGeneratedFormats([formatKey]);
    if (existingFormats.length > 0) {
      setPendingRegeneration({
        kind: "single",
        mode,
        formatKey,
        existingFormats,
        regenerationObjection: currentObjection,
      });
      return;
    }

    await startSingleFormatGeneration(mode, currentObjection);
  }

  function confirmRegeneration() {
    const request = pendingRegeneration;
    setPendingRegeneration(null);
    if (!request) return;

    if (request.kind === "all") {
      void startAllFormatsGeneration(request.regenerationObjection);
      return;
    }

    void startSingleFormatGeneration(request.mode, request.regenerationObjection);
  }

  /**
   * Start a real backend content-generation package job for the requested formats.
   */
  async function startRequestedFormatsGeneration(
    requestedFormats: ContentFormatKey[],
    regenerationObjection?: string | null
  ) {
    if (!videoResult || isRequestingPackage || requestedFormats.length === 0) return;
    setIsRequestingPackage(true);
    setPackageJobId(null);
    setPackageJobError(null);
    setPackageRequestedFormats(requestedFormats);
    try {
      const response = await requestPackage(
        videoResult.id,
        {
          clientId: selectedClientId || null,
          requestedFormats,
          regenerationObjection: regenerationObjection ?? null,
        },
        { token: effectiveToken }
      );
      setPackageRequestedFormats(requestedFormats);
      setPackageJobId(response.jobId);
      setPackageJobClientId(selectedClientId || null);
      if (regenerationObjection) {
        setRegenerationObjection("");
      }
      setRegenerationChoiceTarget(null);
      setRegenerationObjectionTarget(null);
    } catch (e) {
      const err = e as { message?: string };
      setPackageJobError({ message: err?.message ?? "Failed to start content generation." });
    } finally {
      setIsRequestingPackage(false);
    }
  }

  async function startAllFormatsGeneration(regenerationObjection?: string | null) {
    await startRequestedFormatsGeneration(SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS, regenerationObjection);
  }

  async function handleConvertAll(includeObjection = false) {
    if (!videoResult || isRequestingPackage) return;
    const currentObjection = resolveRegenerationObjection(includeObjection);
    if (includeObjection && !currentObjection) return;
    const existingFormats = getExistingGeneratedFormats(SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS);
    if (existingFormats.length > 0) {
      setPendingRegeneration({
        kind: "all",
        formats: SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS,
        existingFormats,
        regenerationObjection: currentObjection,
      });
      return;
    }

    await startAllFormatsGeneration(currentObjection);
  }


  // Reset package job state when the analysis changes. If we're loading from history and the
  // user picked a row that already has a content-generation job, the click handler stashes that
  // jobId (and its clientId) in pendingPackageReattachRef so the panel reattaches instead of
  // "starting fresh".
  // IMPORTANT: When videoResult becomes null (resetVideoState), we must NOT consume the ref —
  // handleLoadFromHistory sets the ref synchronously before clearing videoResult, so if we
  // consumed it on the null transition we'd lose the staged data before setVideoResult fires.
  useEffect(() => {
    if (!videoResult?.id) {
      // Analysis cleared; just reset job state. Leave the ref intact so the next
      // setVideoResult(result) call can pick it up correctly.
      setPackageJobId(null);
      setPackageJobClientId(null);
      setIsReattachingJob(false);
      setPackageRequestedFormats(SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS);
      setPackageJobError(null);
      return;
    }
    // videoResult has a real id — consume the pending reattach ref.
    const { jobId, clientId } = pendingPackageReattachRef.current;
    pendingPackageReattachRef.current = { jobId: null, clientId: null };
    setPackageJobId(jobId);
    setPackageJobClientId(clientId);
    setIsReattachingJob(jobId !== null);
    setPackageRequestedFormats(SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS);
    setPackageJobError(null);
  }, [videoResult?.id]);

  // Clear the reattach guard once the initial status fetch completes so that subsequent
  // real generation jobs can drive the overlay correctly.
  useEffect(() => {
    if (!packageJob.isLoading) {
      setIsReattachingJob(false);
    }
  }, [packageJob.isLoading]);

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
      setIsSavedAnalysisView(true);
      setIsVideoAnalysisCollapsed(true);
      setError(null);
      pendingPackageReattachRef.current = {
        jobId: item.latestContentGeneration?.jobId ?? null,
        clientId: item.clientId ?? null,
      };
      try {
        const current = await getSingleContentAnalysis(item.id, effectiveToken);
        if (current.snapshot) {
          const result = mapBackendResponseToResult(current, selectedOrganizationId);
          setVideoResult(result);
          setSelectedClientId(current.clientId ?? "");
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
        setIsSavedAnalysisView(false);
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

  async function resumeDuplicateAnalysis() {
    if (!duplicateAnalysisPrompt) return;
    const item = duplicateAnalysisPrompt;
    setDuplicateAnalysisPrompt(null);
    setUrlInput(item.normalizedUrl ?? item.sourceUrl);
    await handleLoadFromHistory(item);
  }

  const handleDeleteFromHistory = useCallback(
    async (item: SingleContentAnalysisHistoryItem) => {
      if (!effectiveToken) return;
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

  const requestDeleteFromHistory = useCallback((item: SingleContentAnalysisHistoryItem) => {
    setPendingDeleteHistoryItem(item);
  }, []);

  function confirmDeleteFromHistory() {
    const item = pendingDeleteHistoryItem;
    setPendingDeleteHistoryItem(null);
    if (!item) return;
    void handleDeleteFromHistory(item);
  }

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

  function activeContent(): string | null {
    const formatKey = formatForMutationMode();
    const content = selectedClientLocalFormats[formatKey]?.content?.trim();
    return content && content.length > 0 ? content : null;
  }

  const pendingRegenerationLabels =
    pendingRegeneration?.existingFormats.map((format) => FORMAT_LABELS[format]).join(", ") ?? "";
  const selectedFormatKey = formatForMutationMode();
  const selectedFormatRegenerationTarget: RegenerationTarget = {
    kind: "single",
    mode: mutationMode,
    formatKey: selectedFormatKey,
  };
  const allFormatsRegenerationTarget: RegenerationTarget = { kind: "all" };
  const hasExistingFormatForCurrentMode =
    getExistingGeneratedFormats([selectedFormatKey]).length > 0;
  const shouldShowSavedAnalysisActions = isSavedAnalysisView && isAnalyzed && Boolean(videoResult);
  const isSelectedFormatChoiceOpen =
    isSameRegenerationTarget(regenerationChoiceTarget, selectedFormatRegenerationTarget);
  const isAllFormatsChoiceOpen =
    isSameRegenerationTarget(regenerationChoiceTarget, allFormatsRegenerationTarget);
  const isSelectedFormatObjectionOpen =
    isSameRegenerationTarget(regenerationObjectionTarget, selectedFormatRegenerationTarget) &&
    hasExistingFormatForCurrentMode;
  const isAllFormatsObjectionOpen =
    isSameRegenerationTarget(regenerationObjectionTarget, allFormatsRegenerationTarget) &&
    getExistingGeneratedFormats(SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS).length > 0;
  const videoCardTitle =
    videoResult?.title?.trim() || videoResult?.primaryHook?.trim() || "Untitled";
  const normalizedVideoTitle = videoResult?.title?.trim() ?? "";
  const normalizedVideoDescription = videoResult?.description?.trim() ?? "";
  const descriptionMatchesTitle =
    normalizedVideoTitle.length > 0 &&
    normalizedVideoDescription.length > 0 &&
    normalizedVideoDescription.toLowerCase() === normalizedVideoTitle.toLowerCase();
  const shouldHideDuplicatedDescription =
    !(videoResult?.primaryHook?.trim().length ?? 0) && descriptionMatchesTitle;
  const shouldHideVideoDescription = Boolean(videoResult?.isFallbackThumbnail);
  const shouldShowVideoDescription =
    Boolean(videoResult?.description) &&
    !shouldHideDuplicatedDescription &&
    !shouldHideVideoDescription;

  const handleVideoThumbnailError = useCallback((event: SyntheticEvent<HTMLImageElement>) => {
    if (event.currentTarget.src.includes(FALLBACK_THUMBNAIL_PATH)) return;
    event.currentTarget.src = FALLBACK_THUMBNAIL_PATH;
  }, []);

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col gap-6 p-6 max-w-screen-xl mx-auto">
      {/* Header */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-3">
          <div className="rounded-lg bg-purple-500/10 p-2.5">
            <ScanSearch className="h-5 w-5 text-purple-400" />
          </div>
          <div>
            <h1 className="text-xl font-semibold text-foreground">Single Analyzer</h1>
            <p className="text-sm text-muted-foreground">Load any video · analyze · convert</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
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

          <AnalyzerToolsToolbar
            showResultActions={shouldShowSavedAnalysisActions}
            onHelp={openAnalyzerHelp}
            onSave={saveCurrentWork}
            onNew={startNewAnalysis}
            actionsDisabled={isLoading || packageGenerationIsRunning}
            saveLabel="Save work"
            newLabel="New analysis"
          />
        </div>
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
        mode="progress"
      />
      <AnalysisOverlay
        visible={analysisStatus !== null}
        steps={ANALYSIS_STEPS}
        currentStep={statusToStepIndex(analysisStatus)}
        percent={statusToPercent(analysisStatus)}
        isDone={analysisStatus === "Analyzed"}
        isFailed={analysisStatus === "Failed"}
        runningTitle="Analyzing content"
        doneTitle="Analysis complete"
        failedTitle="Analysis failed"
        description="This usually takes 30–60 seconds. Keep this tab open."
        ariaLabel="Analyzing content"
      />
      <PackageGenerationOverlay
        visible={packageGenerationIsRunning}
        status={packageUiStatus}
        perFormatStatus={packageJob.perFormatStatus}
        totalFormats={packageRequestedFormats.length}
      />

      {/* Main 2-col grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* ── LEFT PANEL ── */}
        <div className="flex flex-col gap-4">
          {/* Input card */}
          {isSavedAnalysisView ? (
            <Card className="border-border/40 bg-background/60">
              <CardContent className="py-4">
                <p className="text-xs text-muted-foreground/80">
                  Saved analysis loaded. Analyze by URL/Search is hidden in this mode. You can regenerate content from this analysis.
                </p>
              </CardContent>
            </Card>
          ) : (
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
                          if (e.key === "Enter") void loadVideoByUrl();
                        }}
                        className="h-8 text-xs"
                      />
                      <Button
                        size="sm"
                        onClick={() => void loadVideoByUrl()}
                        disabled={isLoading || isCheckingDuplicate || !canAnalyzeByUrl}
                        className="h-8 px-3 text-xs"
                      >
                        {isLoading || isCheckingDuplicate ? (
                          <RefreshCw className="h-3 w-3 animate-spin" />
                        ) : (
                          "Analyze"
                        )}
                      </Button>
                    </div>
                    <div className="flex items-center justify-between gap-2">
                      <p className="text-xs text-muted-foreground/70 flex-1">
                        {isCheckingDuplicate
                          ? "Checking saved analyses before starting a new provider run."
                          : isLoading
                          ? "Analyzing… fetching and processing the video can take up to 60 seconds."
                          : detectedPlatform === "unknown"
                          ? "Unsupported URL for this analyzer. Paste a supported social URL."
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
                              ? "Unsupported URL for this analyzer."
                              : `Detected: ${PLATFORM_LABELS[detectedPlatform]}`
                          }
                        >
                          {PLATFORM_LABELS[detectedPlatform]}
                        </Badge>
                      )}
                    </div>
                    {duplicateAnalysisPrompt && (
                      <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 space-y-2">
                        <div className="flex items-start gap-2">
                          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
                          <div className="min-w-0 flex-1 space-y-1">
                            <p className="text-xs font-medium text-foreground">
                              This video was already analyzed
                            </p>
                            <p className="text-xs text-muted-foreground/75">
                              Saved on {formatDate(duplicateAnalysisPrompt.createdAtUtc)}. Resume the saved
                              analysis to regenerate content, or run a new analysis from scratch.
                            </p>
                          </div>
                        </div>
                        <div className="flex flex-wrap gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            className="h-8 text-xs"
                            onClick={() => void loadVideoByUrl({ force: true })}
                            disabled={isLoading}
                          >
                            Analyze again
                          </Button>
                          <Button
                            size="sm"
                            className="h-8 text-xs"
                            onClick={() => void resumeDuplicateAnalysis()}
                            disabled={isLoading}
                          >
                            Resume saved analysis
                          </Button>
                        </div>
                      </div>
                    )}
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
          )}

          {/* Video card */}
          {videoResult && (
            <Card className="border-border/40 bg-background/60 overflow-hidden">
              <div className="relative aspect-video w-full overflow-hidden bg-muted/30">
                <img
                  src={videoResult.thumbnailUrl}
                  alt={videoCardTitle}
                  className="w-full h-full object-cover"
                  onError={handleVideoThumbnailError}
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
                  <h3 className="text-sm font-semibold text-foreground line-clamp-2">{videoCardTitle}</h3>
                  <p className="mt-0.5 text-xs text-muted-foreground">{videoResult.author}</p>
                  {shouldShowVideoDescription && (
                    <div className="mt-2 space-y-1">
                      <p
                        className={cn(
                          "text-xs leading-relaxed text-muted-foreground/80",
                          !isDescriptionExpanded && "line-clamp-2"
                        )}
                      >
                        {videoResult.description}
                      </p>
                      {normalizedVideoDescription.length > 140 && (
                        <Button
                          size="sm"
                          variant="ghost"
                          className="h-7 px-2 text-xs text-muted-foreground"
                          onClick={() => setIsDescriptionExpanded((value) => !value)}
                        >
                          {isDescriptionExpanded ? "Show less" : "Show full description"}
                        </Button>
                      )}
                    </div>
                  )}
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
                <CardHeader className="pb-3 space-y-3">
                <TabBar
                  tabs={[
                    { key: "conversion", label: "Convert" },
                    { key: "preview", label: "Preview" },
                    { key: "package", label: "Package" },
                  ]}
                  active={rightPanelTab}
                  onChange={(k) => setRightPanelTab(k as "conversion" | "preview" | "package")}
                />
                  {/* Client context selector — shown on Convert and Preview tabs only.
                      The Packages tab shows ALL clients' folders without filtering by client.
                      INC-6 REQ-5: selector MUST NOT appear on the Packages tab. */}
                  {rightPanelTab !== "package" && (
                    <OptionCardSelector
                      label="Client context"
                      value={selectedClientId}
                      options={clientContextOptions}
                      onChange={changeClientContext}
                      noneOption={{
                        value: "",
                        label: "No client context",
                        description: "Analysis only",
                        icon: <Building2 className="h-4 w-4" />,
                      }}
                      loading={clientsLoading}
                      loadingLabel="Loading clients..."
                      emptyLabel="No active clients"
                      selectionLabel="Single selection"
                    />
                  )}
                </CardHeader>
                <CardContent className="pt-0">
                {rightPanelTab === "conversion" && (
                  hasClientContext ? (
                  <div className="flex flex-col gap-4">
                    {/* Backend package pipeline: enqueue all formats, poll live status, then download zip. */}
                    <div className="space-y-2 order-2">
                      <div className="rounded-lg border border-purple-500/20 bg-purple-500/5 p-3 space-y-2">
                        <div className="flex items-start gap-2">
                          <Sparkles className="h-4 w-4 text-purple-400 mt-0.5 shrink-0" />
                          <div className="flex-1 min-w-0">
                            <p className="text-xs font-medium text-foreground">Convert to all formats</p>
                            <p className="text-xs text-muted-foreground/70">
                              Generate the active formats on this screen: script, carousel, and flyer.
                              Download the <code className="text-[10px]">package.zip</code> when ready.
                            </p>
                          </div>
                        </div>
                        <div className="flex gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            className="flex-1 h-8 text-xs"
                            onClick={() => {
                              if (shouldRegenerateAllFormats) {
                                toggleRegenerationChoices(allFormatsRegenerationTarget);
                                return;
                              }

                              void handleConvertAll();
                            }}
                            disabled={
                                packageGenerationIsRunning ||
                                packageGenerationClientMissing
                              }
                            title={
                              shouldRegenerateAllFormats
                                ? "Regenerate all formats. The latest generated content will be replaced after confirmation."
                                : packageGenerationClientMissing
                                ? "Pick a client before generating branded content."
                                : undefined
                            }
                          >
                            {packageGenerationIsRunning ? (
                              <span className="flex items-center gap-2">
                                <RefreshCw className="h-3 w-3 animate-spin" />
                                Generating all formats…
                              </span>
                            ) : shouldRegenerateAllFormats ? (
                              <span className="flex items-center gap-2">
                                <RefreshCw className="h-3 w-3" />
                                Regenerate all formats
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
                        </div>
                        {isAllFormatsChoiceOpen && (
                          <RegenerationChoicePanel
                            onRegenerate={() => startSimpleRegeneration(allFormatsRegenerationTarget)}
                            onWithObjections={() => showRegenerationObjection(allFormatsRegenerationTarget)}
                            disabled={
                              packageGenerationIsRunning ||
                              packageGenerationClientMissing
                            }
                          />
                        )}
                        {isAllFormatsObjectionOpen && (
                          <RegenerationObjectionEditor
                            value={regenerationObjection}
                            onChange={setRegenerationObjection}
                            onCancel={() => {
                              setRegenerationObjection("");
                              setRegenerationObjectionTarget(null);
                            }}
                            onSubmit={() => submitRegenerationWithObjection(allFormatsRegenerationTarget)}
                            disabled={
                              packageGenerationIsRunning ||
                              packageGenerationClientMissing
                            }
                            submitLabel="Regenerate all with objections"
                          />
                        )}
                        {convertAllPills.length > 0 && (
                          <div className="flex flex-wrap gap-1 pt-1">
                            {convertAllPills.map(({ format: fmt, status: st }) => (
                              <span
                                key={fmt}
                                className={cn(
                                  "inline-flex items-center rounded px-1.5 py-0.5 text-[10px] border",
                                  st === "Rendered" || st === "Succeeded" || st === "Generated"
                                    ? "bg-green-500/10 text-green-600 dark:text-green-400 border-green-500/20"
                                    : st === "Failed"
                                    ? "bg-red-500/10 text-red-600 dark:text-red-400 border-red-500/20"
                                    : "bg-muted/40 text-muted-foreground border-border/30"
                                )}
                              >
                                {FORMAT_LABELS[fmt]} · {st === "Rendered" || st === "Succeeded" ? "generated" : st.toLowerCase()}
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

                    <div className="order-1 space-y-3">
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

                      {/* Per-format generate section */}
                      <div>
                      <div className="mt-3 space-y-3">
                        {activeContent() ? (
                          <div className="rounded-lg border border-border/40 bg-muted/20 p-3 space-y-3">
                            <pre className="whitespace-pre-wrap text-xs text-foreground/80 font-mono leading-relaxed">
                              {activeContent()}
                            </pre>
                            <div className="space-y-2">
                              <Button
                                size="sm"
                                variant="outline"
                                className="h-8 text-xs"
                                onClick={() => toggleRegenerationChoices(selectedFormatRegenerationTarget)}
                                disabled={
                                  generatingConversion !== null ||
                                  packageGenerationIsRunning ||
                                  packageGenerationClientMissing
                                }
                              >
                                <RefreshCw className="h-3 w-3" />
                                Regenerate {mutationMode}
                              </Button>
                              {isSelectedFormatChoiceOpen && (
                                <RegenerationChoicePanel
                                  onRegenerate={() => startSimpleRegeneration(selectedFormatRegenerationTarget)}
                                  onWithObjections={() => showRegenerationObjection(selectedFormatRegenerationTarget)}
                                  disabled={
                                    generatingConversion !== null ||
                                    packageGenerationIsRunning ||
                                    packageGenerationClientMissing
                                  }
                                />
                              )}
                              {isSelectedFormatObjectionOpen && (
                                <RegenerationObjectionEditor
                                  value={regenerationObjection}
                                  onChange={setRegenerationObjection}
                                  onCancel={() => {
                                    setRegenerationObjection("");
                                    setRegenerationObjectionTarget(null);
                                  }}
                                  onSubmit={() => submitRegenerationWithObjection(selectedFormatRegenerationTarget)}
                                  disabled={
                                    generatingConversion !== null ||
                                    packageGenerationIsRunning ||
                                    packageGenerationClientMissing
                                  }
                                  submitLabel={`Regenerate ${mutationMode} with objections`}
                                />
                              )}
                              {packageJob.manifest?.has_stored_assets &&
                                packageSucceededFormats.includes(selectedFormatKey) && (
                                  <Button
                                    size="sm"
                                    variant="destructive"
                                    className="h-8 text-xs"
                                    onClick={() => void handleDeleteFormat(selectedFormatKey)}
                                    disabled={isFormatMutating || packageGenerationIsRunning}
                                    title="Permanently delete the stored asset for this format."
                                  >
                                    <Trash2 className="h-3 w-3" />
                                    Delete
                                  </Button>
                                )}
                            </div>
                          </div>
                        ) : (
                          <div className="space-y-2">
                            <Button
                              size="sm"
                              variant="outline"
                              className="w-full h-8 text-xs"
                              onClick={() => {
                                if (hasExistingFormatForCurrentMode) {
                                  toggleRegenerationChoices(selectedFormatRegenerationTarget);
                                  return;
                                }

                                void handleGenerate();
                              }}
                              disabled={
                                generatingConversion !== null ||
                                packageGenerationIsRunning ||
                                packageGenerationClientMissing
                              }
                            >
                              {generatingConversion === mutationMode ? (
                                <span className="flex items-center gap-2">
                                  <RefreshCw className="h-3 w-3 animate-spin" />
                                  Generating…
                                </span>
                              ) : hasExistingFormatForCurrentMode ? (
                                `Regenerate ${mutationMode}`
                              ) : (
                                `Generate ${mutationMode}`
                              )}
                            </Button>
                            {hasExistingFormatForCurrentMode && isSelectedFormatChoiceOpen && (
                              <RegenerationChoicePanel
                                onRegenerate={() => startSimpleRegeneration(selectedFormatRegenerationTarget)}
                                onWithObjections={() => showRegenerationObjection(selectedFormatRegenerationTarget)}
                                disabled={
                                  generatingConversion !== null ||
                                  packageGenerationIsRunning ||
                                  packageGenerationClientMissing
                                }
                              />
                            )}
                            {isSelectedFormatObjectionOpen && (
                              <RegenerationObjectionEditor
                                value={regenerationObjection}
                                onChange={setRegenerationObjection}
                                onCancel={() => {
                                  setRegenerationObjection("");
                                  setRegenerationObjectionTarget(null);
                                }}
                                onSubmit={() => submitRegenerationWithObjection(selectedFormatRegenerationTarget)}
                                disabled={
                                  generatingConversion !== null ||
                                  packageGenerationIsRunning ||
                                  packageGenerationClientMissing
                                }
                                submitLabel={`Regenerate ${mutationMode} with objections`}
                              />
                            )}
                            </div>
                        )}
                      </div>
                    </div>
                  </div>
                  </div>
                  ) : (
                    <div className="flex min-h-[220px] flex-col items-center justify-center rounded-md border border-dashed border-border/40 bg-muted/10 p-6 text-center">
                      <Building2 className="h-8 w-8 text-muted-foreground/35" />
                      <p className="mt-3 text-sm text-muted-foreground/70">
                        Select a client context to generate branded content.
                      </p>
                    </div>
                  )
                )}

                {rightPanelTab === "preview" && (
                  <div>
                    <div className="mt-3">
                      {activeContent() ? (
                        <SocialTabletPreview
                          content={activeContent() ?? ""}
                          network={videoResult?.networkSource ?? "social"}
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

                {rightPanelTab === "package" && (
                  <PackageTabContent
                    localFormats={selectedClientLocalFormats}
                    packageSucceededFormats={packageSucceededFormats}
                    manifest={packageJob.manifest ?? null}
                    isLoading={
                      packageGenerationIsRunning ||
                      isRequestingPackage ||
                      isDownloadingPackage ||
                      isLoadingPackages
                    }
                    isJobFetching={packageJob.isLoading && isReattachingJob}
                    canDownloadPackage={canDownloadPackage}
                    hasAnyGeneratedFormat={hasAnyGeneratedFormat}
                    clientFolderName={
                      packageJob.manifest?.client_folder_name ??
                      (selectedClientId
                        ? deriveClientFolderSlug(selectedClient?.name ?? "")
                        : null)
                    }
                    clientPackages={clientPackages}
                    clientLabels={Object.fromEntries(
                      clients.map((c) => [c.id, deriveClientFolderSlug(c.name)])
                    )}
                    onDownload={() => void handlePackageAction()}
                  />
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
                  onDelete={() => requestDeleteFromHistory(item)}
                />
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      <ConfirmationDialog
        open={pendingDeleteHistoryItem !== null}
        title="Delete this analysis?"
        description="The scrape, AI analysis, generated formats, and downloadable packages will be permanently removed."
        cancelLabel="Keep analysis"
        confirmLabel="Delete permanently"
        confirmVariant="destructive"
        onCancel={() => setPendingDeleteHistoryItem(null)}
        onConfirm={confirmDeleteFromHistory}
      />

      <ConfirmationDialog
        open={pendingRegeneration !== null}
        title="Replace generated content?"
        description={
          pendingRegenerationLabels
            ? `Regenerating ${pendingRegenerationLabels} will replace the current generated content for this analysis.${pendingRegeneration?.regenerationObjection ? " Your objection notes will be sent to the agent." : ""}`
            : `Regenerating will replace the current generated content for this analysis.${pendingRegeneration?.regenerationObjection ? " Your objection notes will be sent to the agent." : ""}`
        }
        cancelLabel="Keep current content"
        confirmLabel="Regenerate and replace"
        confirmVariant="destructive"
        onCancel={() => setPendingRegeneration(null)}
        onConfirm={confirmRegeneration}
      />

      <ConfirmationDialog
        open={isAbandonDialogOpen}
        title="Leave this page?"
        description="You have local regeneration notes that have not been sent to the API. If you leave now, this draft will be discarded."
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

// ── PackageTabContent ──────────────────────────────────────────────────────────
interface PackageTabContentProps {
  localFormats: Partial<Record<ContentFormatKey, LocalFormatEntry>>;
  packageSucceededFormats: ContentFormatKey[];
  manifest: GeneratedPackageManifest | null;
  isLoading: boolean;
  /** True while re-attaching an existing job from history (suppresses "no content" empty state). */
  isJobFetching: boolean;
  canDownloadPackage: boolean;
  hasAnyGeneratedFormat: boolean;
  /**
   * Normalized client subfolder from the backend manifest (e.g. "acme-corp").
   * Present only for packages generated after the per-client folder feature.
   * Null for legacy flat-layout packages.
   */
  clientFolderName: string | null;
  /**
   * INC-6: Multi-client package tree from the backend DB listing.
   * Keyed by clientId → { formats, assets }.
   * When present and non-empty, the Packages tab renders one folder per client
   * with carousel slide children, instead of the single-client view.
   */
  clientPackages: import("@/lib/hooks/use-package-workspace").ClientPackages;
  /** INC-6: Maps clientId → human-readable folder slug, so the tree shows the client
   *  instead of an opaque GUID. Falls back to a truncated id when a client is unknown. */
  clientLabels: Record<string, string>;
  onDownload: () => void;
}

function PackageTabContent({
  localFormats,
  packageSucceededFormats,
  manifest,
  isLoading,
  isJobFetching,
  canDownloadPackage,
  hasAnyGeneratedFormat,
  clientFolderName,
  clientPackages,
  clientLabels,
  onDownload,
}: PackageTabContentProps) {
  const allFormatKeys = Array.from(
    new Set([...Object.keys(localFormats) as ContentFormatKey[], ...packageSucceededFormats])
  );

  // INC-6: Determine whether we have a multi-client tree from the backend.
  const clientPackageEntries = Object.values(clientPackages);
  const hasMultiClientTree = clientPackageEntries.length > 0;

  // Show a loading skeleton while the hook is fetching the reattached job status.
  if (allFormatKeys.length === 0 && !hasMultiClientTree && isJobFetching) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[160px] text-center text-xs text-muted-foreground/60 space-y-2 py-6">
        <RefreshCw className="h-6 w-6 opacity-40 animate-spin" />
        <p>Loading package contents…</p>
      </div>
    );
  }

  if (allFormatKeys.length === 0 && !hasMultiClientTree) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[160px] text-center text-xs text-muted-foreground/60 space-y-2 py-6">
        <Package className="h-8 w-8 opacity-30" />
        <p>No content generated yet.</p>
        <p className="text-[10px]">Use the Convert tab to generate formats.</p>
      </div>
    );
  }

  const hasBackendPackage = Boolean(manifest) || hasAnyGeneratedFormat || hasMultiClientTree;

  return (
    <div className="flex flex-col gap-4 py-2">
      {/* File tree */}
      <div className="rounded-lg border border-border/40 bg-muted/20 p-3 space-y-1">
        <p className="text-[10px] font-medium text-muted-foreground/60 uppercase tracking-wide mb-2">
          Package contents
        </p>

        {/* Root folder label */}
        <div className="flex items-center gap-1.5 text-xs text-muted-foreground/70 mb-1">
          <FolderOpen className="h-3.5 w-3.5 text-amber-400" />
          <span className="font-mono">package/</span>
        </div>

        {/* INC-6: Multi-client tree from the backend DB listing.
            When clientPackages has entries, render one folder per client with
            format files and carousel slide children. */}
        {hasMultiClientTree ? (
          clientPackageEntries.map((clientEntry) => (
            <div key={clientEntry.clientId} className="pl-4 mb-1">
              {/* Client folder */}
              <div className="flex items-center gap-1.5 text-xs text-muted-foreground/70 mb-0.5">
                <FolderOpen className="h-3.5 w-3.5 text-amber-300 shrink-0" />
                <span className="font-mono text-xs truncate" title={clientEntry.clientId}>
                  {clientLabels[clientEntry.clientId] ?? `${clientEntry.clientId.substring(0, 8)}…`}/
                </span>
              </div>
              {/* Format files — indented inside client folder */}
              {clientEntry.formats.map((formatEntry) => {
                const isCarousel = formatEntry.formatKey === "carousel";
                return (
                  <div key={formatEntry.formatKey}>
                    {/* Format row */}
                    <div className="pl-6 flex items-center gap-1.5 py-0.5">
                      {isCarousel ? (
                        <FolderOpen className="h-3.5 w-3.5 text-amber-400 shrink-0" />
                      ) : formatEntry.filename.endsWith(".png") || formatEntry.filename.endsWith(".jpg") ? (
                        <FileImage className="h-3.5 w-3.5 text-blue-400 shrink-0" />
                      ) : (
                        <FileText className="h-3.5 w-3.5 text-muted-foreground/60 shrink-0" />
                      )}
                      <span className="font-mono text-xs text-foreground/80 truncate">
                        {isCarousel ? "carousel/" : formatEntry.filename}
                      </span>
                      <span className="ml-auto inline-flex items-center rounded px-1.5 py-0.5 text-[10px] border bg-green-500/10 text-green-600 dark:text-green-400 border-green-500/20 shrink-0">
                        Generated
                      </span>
                    </div>
                    {/* Carousel slide children */}
                    {isCarousel && formatEntry.assets.map((asset) => (
                      <div key={asset.index} className="pl-12 flex items-center gap-1.5 py-0.5">
                        <FileImage className="h-3 w-3 text-blue-300 shrink-0" />
                        <span className="font-mono text-xs text-muted-foreground/70 truncate">
                          {asset.filename}
                        </span>
                      </div>
                    ))}
                  </div>
                );
              })}
            </div>
          ))
        ) : (
          <>
            {/* Legacy single-client tree (backward compat: no clientPackages yet) */}
            {clientFolderName && (
              <div className="pl-4 mb-0.5">
                <div className="flex items-center gap-1.5 text-xs text-muted-foreground/70">
                  <FolderOpen className="h-3.5 w-3.5 text-amber-300" />
                  <span className="font-mono">{clientFolderName}/</span>
                </div>
              </div>
            )}

            {/* Format entries — indented further when inside a client folder */}
            {allFormatKeys.map((formatKey) => {
              const local = localFormats[formatKey];
              const isSucceeded = packageSucceededFormats.includes(formatKey);
              const filename = resolveFormatFilename(
                formatKey,
                manifest?.formats?.[formatKey]?.filename
              );
              const isFolder = filename.endsWith("/");
              const isRegenerated = (local?.regenerationCount ?? 0) > 0;
              const indent = clientFolderName ? "pl-10" : "pl-4";

              return (
                <div key={formatKey} className={`flex items-start gap-2 ${indent} py-0.5`}>
                  <div className="flex items-center gap-1.5 flex-1 min-w-0">
                    {isFolder ? (
                      <FolderOpen className="h-3.5 w-3.5 text-amber-400 shrink-0" />
                    ) : filename.endsWith(".png") || filename.endsWith(".jpg") ? (
                      <FileImage className="h-3.5 w-3.5 text-blue-400 shrink-0" />
                    ) : (
                      <FileText className="h-3.5 w-3.5 text-muted-foreground/60 shrink-0" />
                    )}
                    <span className="font-mono text-xs text-foreground/80 truncate">{filename}</span>
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    {isRegenerated && (
                      <span className="inline-flex items-center rounded px-1.5 py-0.5 text-[10px] border bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/20">
                        Regenerated ×{local!.regenerationCount}
                      </span>
                    )}
                    {local?.generatedAt && (
                      <span className="text-[10px] text-muted-foreground/50 tabular-nums">
                        {new Date(local.generatedAt).toLocaleTimeString([], {
                          hour: "2-digit",
                          minute: "2-digit",
                        })}
                      </span>
                    )}
                    {isSucceeded && !local && (
                      <span className="inline-flex items-center rounded px-1.5 py-0.5 text-[10px] border bg-green-500/10 text-green-600 dark:text-green-400 border-green-500/20">
                        Generated
                      </span>
                    )}
                    {isSucceeded && local && (
                      <CheckCircle2 className="h-3 w-3 text-green-500" />
                    )}
                  </div>
                </div>
              );
            })}
          </>
        )}

        {/* manifest.json is always bundled with every backend-generated package */}
        {hasBackendPackage && (
          <div className="flex items-start gap-2 pl-4 py-0.5">
            <div className="flex items-center gap-1.5 flex-1 min-w-0">
              <FileText className="h-3.5 w-3.5 text-muted-foreground/40 shrink-0" />
              <span className="font-mono text-xs text-muted-foreground/50 truncate">manifest.json</span>
            </div>
            <span className="text-[10px] text-muted-foreground/40 shrink-0">system</span>
          </div>
        )}
      </div>

      {/* Download button */}
      <Button
        size="sm"
        className="w-full h-9 text-xs"
        onClick={onDownload}
        disabled={!hasAnyGeneratedFormat || !canDownloadPackage || isLoading}
        title={
          !hasAnyGeneratedFormat
            ? "Generate at least one format first."
            : !canDownloadPackage
            ? "Package is not ready yet."
            : "Download the generated package."
        }
      >
        {isLoading ? (
          <span className="flex items-center gap-2">
            <RefreshCw className="h-3 w-3 animate-spin" />
            Downloading…
          </span>
        ) : (
          <span className="flex items-center gap-2">
            <Package className="h-3 w-3" />
            Download package.zip
          </span>
        )}
      </Button>
    </div>
  );
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
