"use client";

import { useDateFormatter } from "@/lib/format/use-date-formatter";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode, SyntheticEvent } from "react";
import { useRouter } from "next/navigation";
import { AnimatePresence, motion } from "framer-motion";
import {
  Database,
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
  ExternalLink,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { StageOverlay } from "@/components/ui/stage-overlay";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog";
import { Input } from "@/components/ui/input";
import { OptionCardSelector, type OptionCardSelectorOption } from "@/components/ui/option-card-selector";
import { Select } from "@/components/ui/select";
import { getMockClients, getMockOrganizations } from "@/lib/mocks/content-workflow";
import {
  getContentWorkflowStatus,
  listContentWorkflowResults,
  listWorkflowClients,
  startContentWorkflow,
} from "@/lib/api/content-workflow";
import { getOrganizationIntegration, listOrganizationIntegrations } from "@/lib/api/integrations";
import type {
  Organization,
  SingleContentAnalysisStatus,
  SingleContentAnalysisHistoryItem,
  SocialClient,
  ContentFormatKey,
  ContentGenerationJobStatus,
  ContentWorkflowStatus,
  ContentWorkflowStep,
  ContentWorkflowStrategyMode,
  GeneratedPackageManifest,
  PackageActionState,
} from "@/lib/api/types";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { useSocialNetworks } from "@/hooks/use-social-networks";
import { useAccessToken } from "@/components/auth/use-access-token";
import { ContentStudioPanel } from "@/components/social/content-studio-panel";
import { useToast } from "@/components/ui/toaster";
import { show as showBlockingLoader, hide as hideBlockingLoader } from "@/lib/loading/blocking-loader-store";
import {
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
  deleteAsset,
  downloadFormatAsPptx,
  exportFormatToCanva,
  exportPackageToCanva,
  ALL_FORMAT_KEYS,
  FORMAT_LABELS,
  listFormatCatalog,
  type FormatCatalogEntry,
} from "@/lib/api/content-generation";
import { useContentGenerationJob } from "@/lib/hooks/use-content-generation-job";
import { usePackageWorkspace } from "@/lib/hooks/use-package-workspace";
import { InferredFieldsDisclosure } from "@/components/social/single-analyzer/inferred-fields-disclosure";
import { PartialCompletionBanner } from "@/components/social/single-analyzer/partial-completion-banner";
import { SupportError } from "@/components/support/support-error";
import { openWhatsAppSupport } from "@/lib/support/support";
import { resolveGenerationFailureError } from "@/lib/support/user-facing-errors";
import {
  canStartAnalyzeRequest,
  generationHistoryLabels,
  shouldAutoSwitchToPackage,
  shouldRefreshTerminalHistory,
  packageProgressState,
  pollAttemptDelayMs,
  resolveInitialLoadStageKey,
  INITIAL_LOAD_STAGES,
} from "@/lib/single-analyzer-ui-state";
import { AnalyzerToolsToolbar } from "@/components/social/analyzer-tools-toolbar";
import { BatchGenerationPanel } from "@/components/social/generator/batch-generation-panel";
import { exportElementToPdf } from "@/lib/export/pdf-export";
import {
  ContentDiscoveryShortlist,
  type ContentDiscoveryShortlistCandidate,
} from "@/components/social/discovery/content-discovery-shortlist";
import {
  discoveryOutcomeOffersSettingsLink,
  discoveryStepIndex,
  getDiscoveryOutcomeMessage,
  getNicheSearchBlockedMessage,
  getNicheSearchReadiness,
  isDiscoveryTerminal,
  isNicheSearchBlocked,
  mapDiscoveryResultsToShortlistCandidates,
  resolveDiscoveryOutcome,
  type DiscoveryRunOutcome,
  type NicheSearchReadiness,
} from "@/lib/social/content-discovery-shortlist";
import {
  REFRESH_BILLING_WARNING,
  canStartRefresh,
  getRefreshBlockedReason,
  resolveDefaultSearchNetwork,
  summariseCandidateSource,
  type CandidateSourceSummary,
} from "@/lib/social/niche-candidate-bank";

// ── Async/poll constants ───────────────────────────────────────────────────────

const POLL_INTERVAL_MS = 2500;
const POLL_TIMEOUT_MS = 5 * 60 * 1000; // 5 min hard cap so the overlay can't hang forever
const FALLBACK_THUMBNAIL_PATH = "/images/thumbnail-unavailable.svg";
// Discovery is start + poll, exactly like the analysis flow: the POST only opens the session and the
// scrape runs in the background, because a real scrape does not fit in one HTTP request (production
// runs were aborted by the browser/proxy at ~30 s while the Apify actor kept running and billing).
// Same cadence and same 5-min cap as the analysis poll on purpose — one idiom, not two.
const DISCOVERY_POLL_INTERVAL_MS = POLL_INTERVAL_MS;
const DISCOVERY_POLL_TIMEOUT_MS = POLL_TIMEOUT_MS;
const DISCOVERY_CRITERION_OPTIONS: Array<{ value: "references" | "own" | "niche"; label: string }> = [
  { value: "references", label: "Reference accounts" },
  { value: "own", label: "Own accounts" },
  { value: "niche", label: "Niche search" },
];
const MAX_REGENERATION_OBJECTION_LENGTH = 1000;
const MAX_FORMAT_FAILURE_ATTEMPTS = 2;
const MAX_AI_ANALYSIS_FAILURE_ATTEMPTS = 2;

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

/** Real backend status → the ANALYSIS_STEPS key it belongs to (null while nothing has started yet). */
function statusToStageKey(status: SingleContentAnalysisStatus | null): string | null {
  if (!status) return null;
  const stage = ANALYSIS_STEPS.find((s) => s.statuses.includes(status));
  return stage?.key ?? null;
}

const PACKAGE_STEPS = [
  { key: "queued", label: "Queued" },
  { key: "prepare", label: "Preparing context" },
  { key: "ai", label: "Generating formats" },
  { key: "render", label: "Rendering assets" },
  { key: "zip", label: "Building package.zip" },
] as const;

// packageProgressState lives in @/lib/single-analyzer-ui-state (issue #67, BAPA-STATUS-03) so it
// is unit-testable via the tsx --test runner. PACKAGE_STEPS.length MUST stay in sync with
// PACKAGE_STEP_COUNT there.

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

function formatDuration(seconds: number | null | undefined): string {
  if (typeof seconds !== "number" || seconds <= 0) return "—";
  const mins = Math.floor(seconds / 60);
  const secs = Math.round(seconds % 60);
  return `${mins}:${secs.toString().padStart(2, "0")}`;
}

function formatListForPlaceholder(items: string[]): string {
  const unique = Array.from(new Set(items.map((item) => item.trim()).filter(Boolean)));
  if (unique.length === 0) return "";
  if (unique.length === 1) return unique[0];
  if (unique.length === 2) return `${unique[0]} or ${unique[1]}`;
  return `${unique.slice(0, -1).join(", ")} or ${unique[unique.length - 1]}`;
}

function slugForFilename(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 48) || "content-analysis";
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
  const state = packageProgressState(status, perFormatStatus, totalFormats);
  const activeKey = PACKAGE_STEPS[Math.min(state.currentStep, PACKAGE_STEPS.length - 1)]?.key ?? null;

  return (
    <StageOverlay
      visible={visible}
      stages={PACKAGE_STEPS}
      activeKey={activeKey}
      runningTitle="Generating content package"
      description={state.label}
      ariaLabel="Generating content package"
    />
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
  "story-single",
  "story-carousel",
  "feed-single",
  "feed-carousel",
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

function packageManifestKeyToContentFormatKey(key: string): ContentFormatKey | null {
  if (key === "script") return "reel-script";
  if (key === "story") return "story-single";
  if (key === "feed-post") return "feed-single";
  // Legacy "carousel" stored in old manifests maps to "story-carousel" for display purposes;
  // the assembly path handles the backward-compat on the backend side.
  if (key === "carousel") return "story-carousel";
  return isKnownFormatKey(key) ? key : null;
}

/** Default filenames shown in the Package tab tree when the backend manifest is absent. */
const FORMAT_FILE_NAMES: Record<ContentFormatKey, string> = {
  "reel-script": "script.md",
  "story-single": "story-single.pptx",
  "story-carousel": "story-carousel/",
  "feed-single": "feed-single.pptx",
  "feed-carousel": "feed-carousel/",
  "content-idea": "content-idea.txt",
  "cta-variation": "cta-variation.txt",
  caption: "caption.txt",
  hook: "hook.txt",
};

/**
 * Resolves the display filename for a format. Uses the manifest filename when available,
 * but corrects the extension if the backend returns a wrong one (e.g. reel-script.png → .md).
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

function resolvePackageFormatFolder(formatKey: ContentFormatKey): string {
  return formatKey === "reel-script" ? "script" : formatKey;
}

function resolvePackageFormatFiles(
  formatKey: ContentFormatKey,
  assets: { filename: string; index: number }[]
): string[] {
  if (formatKey === "story-single") return ["story-single.pptx"];
  if (formatKey === "feed-single") return ["feed-single.pptx"];
  if (formatKey === "reel-script") return ["script.md"];
  if (formatKey === "story-carousel" || formatKey === "feed-carousel") {
    const orderedAssets = [...assets].sort((a, b) => a.index - b.index);
    return (orderedAssets.length > 0 ? orderedAssets : [{ index: 1, filename: "slide_1" }])
      .map((_, index) => `slide_${index + 1}.pptx`);
  }
  return [resolveFormatFilename(formatKey)];
}

// ── Client folder slug derivation (mirrors backend NormalizeFolderName) ────────

function RegenerationChoicePanel({
  onRegenerate,
  onWithObjections,
  disabled = false,
  blockedHint,
}: {
  onRegenerate: () => void;
  onWithObjections: () => void;
  disabled?: boolean;
  blockedHint?: string | null;
}) {
  const isBlocked = Boolean(blockedHint);

  return (
    <div className="rounded-md border border-border/40 bg-muted/20 p-2">
      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          variant="outline"
          className="h-8 text-xs"
          onClick={onRegenerate}
          disabled={disabled || isBlocked}
          title={blockedHint ?? undefined}
        >
          <RefreshCw className="h-3 w-3" />
          Regenerate
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="h-8 text-xs"
          onClick={onWithObjections}
          disabled={disabled || isBlocked}
          title={blockedHint ?? undefined}
        >
          <AlertTriangle className="h-3 w-3" />
          With objections
        </Button>
      </div>
      {blockedHint && (
        <p className="mt-2 text-[10px] leading-relaxed text-destructive">
          {blockedHint}
        </p>
      )}
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

const SINGLE_ANALYZER_BLOCKING_LOADER_ID = "single-analyzer.initial-resources";

/**
 * The three root surface selections exposed in the format picker (SCENARIO-12).
 * Script has no layout toggle. Story and Feed each show Single ↔ Carousel.
 */
type MutationSurface = "script" | "story" | "feed";

/**
 * The active layout selection for Story and Feed surfaces.
 * Script surfaces ignore this value.
 */
type MutationLayout = "single" | "carousel";

/**
 * Derived from MutationSurface + MutationLayout; kept for backward-compat with
 * internal helpers that already accept the resolved ContentFormatKey.
 * SCENARIO-12 mapping:
 *   script          → reel-script
 *   story + single  → story-single
 *   story + carousel→ story-carousel
 *   feed  + single  → feed-single
 *   feed  + carousel→ feed-carousel
 */
type MutationMode = "script" | "story-single" | "story-carousel" | "feed-single" | "feed-carousel";

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

/** A pending package deletion awaiting modal confirmation: a whole format folder or one file/slide. */
type PendingPackageDelete =
  | { kind: "format"; clientId: string; formatKey: ContentFormatKey; label: string }
  | {
      kind: "asset";
      clientId: string;
      formatKey: ContentFormatKey;
      index: number;
      filename: string;
    };

function isSameRegenerationTarget(
  left: RegenerationTarget | null,
  right: RegenerationTarget
): boolean {
  if (!left || left.kind !== right.kind) return false;
  if (left.kind === "all" || right.kind === "all") return true;
  return left.mode === right.mode && left.formatKey === right.formatKey;
}

export function SingleAnalyzerClient({ isPlatformAdmin }: SingleAnalyzerClientProps) {
  const { formatDate } = useDateFormatter();
  const router = useRouter();
  const toast = useToast();
  const contentExportRef = useRef<HTMLDivElement>(null);
  const clientsRequestIdRef = useRef(0);
  const historyRequestIdRef = useRef(0);
  const { orgId: activeOrgId, isInitialized: activeOrgInitialized } = useActiveOrg();
  const token = useAccessToken();
  const effectiveToken = token ?? undefined;
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [organizationsInitialized, setOrganizationsInitialized] = useState(false);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState("");
  const [clients, setClients] = useState<SocialClient[]>([]);
  const [selectedClientId, setSelectedClientId] = useState("");
  const [clientsLoading, setClientsLoading] = useState(false);
  const [clientsLoadedOrganizationId, setClientsLoadedOrganizationId] = useState<string | null>(null);
  const { networks: socialNetworks, loading: networksLoading } = useSocialNetworks(effectiveToken);
  const [inputMode, setInputMode] = useState<"url" | "search">("url");
  const [urlInput, setUrlInput] = useState("");
  const [searchNetwork, setSearchNetwork] = useState("");
  const [searchStrategy, setSearchStrategy] = useState("educational");
  // Discovery shortlist (WU-7, design D8/§8.2): the operator picks a criterion, the chain proposes
  // ranked candidates through the EXISTING content-workflow endpoints, and selecting exactly one
  // reuses the EXISTING single-analysis start+poll path below — no new backend transport.
  const [discoveryCriterion, setDiscoveryCriterion] =
    useState<"references" | "own" | "niche">("references");
  const [discoveryCandidates, setDiscoveryCandidates] =
    useState<ContentDiscoveryShortlistCandidate[]>([]);
  const [isDiscovering, setIsDiscovering] = useState(false);
  const [discoveryError, setDiscoveryError] = useState<string | null>(null);
  // null = discovery has never run in this session. Every other value is the resolved terminal
  // outcome, so a provider failure can never render as "No candidates found yet".
  const [discoveryOutcome, setDiscoveryOutcome] = useState<DiscoveryRunOutcome | null>(null);
  // Live progress mirrored from the status endpoint and rendered through the SAME screen-blocking
  // overlay the analysis flow uses (StageOverlay), not a second progress mechanism.
  const [discoverySteps, setDiscoverySteps] = useState<ContentWorkflowStep[]>([]);
  const [discoveryMessage, setDiscoveryMessage] = useState("");
  // The raw backend status the overlay renders. Kept alongside the resolved outcome because the
  // overlay needs it while the session is still running, before an outcome exists.
  const [discoveryStatusValue, setDiscoveryStatusValue] = useState("in_progress");
  // Where the shortlist's candidates came from — the stored bank or a fresh, billed scrape — and how
  // old the stored ones are. Serving cached data silently is how an operator ends up working from
  // stale content without knowing, so this is never inferred: null until the backend reports it.
  const [candidateSource, setCandidateSource] = useState<CandidateSourceSummary | null>(null);
  // null = not checked yet; only relevant for the "niche" criterion (CD-13/D12 — distinct from an
  // empty result set). Readiness is PER NETWORK: a network with no adapter is "unsupported", which is
  // a different answer from "you still have to configure an actor".
  const [nicheSearchReadiness, setNicheSearchReadiness] = useState<NicheSearchReadiness | null>(null);
  const [videoResult, setVideoResult] = useState<SingleAnalyzerResult | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [isAnalyzed, setIsAnalyzed] = useState(false);
  const [analyzingProgress, setAnalyzingProgress] = useState(0);
  const [localFormatsByClient, setLocalFormatsByClient] = useState<
    Record<string, Partial<Record<ContentFormatKey, LocalFormatEntry>>>
  >({});
  const [generatingConversion, setGeneratingConversion] = useState<MutationMode | null>(null);
  const [rightPanelTab, setRightPanelTab] = useState<"conversion" | "package">("conversion");
  const [studioOpen, setStudioOpen] = useState(false);
  const [mutationMode, setMutationMode] = useState<MutationMode>("script");
  // Surface root (3 buttons) + layout toggle (Single/Carousel for Story and Feed).
  const [mutationSurface, setMutationSurface] = useState<MutationSurface>("script");
  const [mutationLayout, setMutationLayout] = useState<MutationLayout>("single");
  // Server-driven format catalog; falls back to static union when unavailable.
  const [formatCatalogEntries, setFormatCatalogEntries] = useState<FormatCatalogEntry[]>([]);
  const [historyItems, setHistoryItems] = useState<SingleContentAnalysisHistoryItem[]>([]);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [historyLoaded, setHistoryLoaded] = useState(false);
  const [deletingHistoryId, setDeletingHistoryId] = useState<string | null>(null);
  const [pendingDeleteHistoryItem, setPendingDeleteHistoryItem] =
    useState<SingleContentAnalysisHistoryItem | null>(null);
  const [duplicateAnalysisPrompt, setDuplicateAnalysisPrompt] =
    useState<SingleContentAnalysisHistoryItem | null>(null);
  const [isCheckingDuplicate, setIsCheckingDuplicate] = useState(false);
  const [pendingRegeneration, setPendingRegeneration] =
    useState<PendingRegenerationRequest | null>(null);
  // Pending package deletion (a format folder or a single file/slide), confirmed via a modal.
  const [pendingPackageDelete, setPendingPackageDelete] =
    useState<PendingPackageDelete | null>(null);
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
  const [isDownloadingPptx, setIsDownloadingPptx] = useState(false);
  const [isExportingToCanva, setIsExportingToCanva] = useState(false);
  // Whether the active org has the Canva connector configured (gates the package "Send to Canva").
  const [canvaConfigured, setCanvaConfigured] = useState(false);
  const [canvaOrgIntegrationId, setCanvaOrgIntegrationId] = useState("");
  const [isExportingPdf, setIsExportingPdf] = useState(false);
  const [isHydratingHistory, setIsHydratingHistory] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState(0);
  const [downloadResult, setDownloadResult] = useState<{ fileName: string; downloadUrl: string } | null>(null);
  const [error, setError] = useState<{ code?: string; message: string } | null>(null);
  // Async/poll state: drives the staged progress overlay during a backend analysis.
  const [analysisStatus, setAnalysisStatus] = useState<SingleContentAnalysisStatus | null>(null);
  const pollCancelRef = useRef(false);
  const lastHistoryRefreshKeyRef = useRef<string | null>(null);
  // Reentrancy guard for loadVideoByUrl: Enter keydown + the Analyze button click can both fire
  // within the same tick, before React repaints `disabled`. A ref (not useState) makes the check
  // synchronous so a second concurrent invocation bails out immediately instead of racing the
  // first one's setAnalysisStatus/setIsLoading calls (see canStartAnalyzeRequest).
  const loadVideoInFlightRef = useRef(false);

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

  // Which formats the operator wants this run to build. Defaults to the full set (previous
  // behavior), but every visual format drives per-slide AI image renders, so being able to drop to
  // one or two formats is the difference between a ~40 minute run and a quick check.
  const [selectedFormatKeys, setSelectedFormatKeys] = useState<ContentFormatKey[]>(
    SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS
  );

  function toggleFormatKey(formatKey: ContentFormatKey) {
    setSelectedFormatKeys((current) =>
      current.includes(formatKey)
        ? current.filter((key) => key !== formatKey)
        // Keep the canonical catalog order regardless of click order, so the per-client piece cap
        // (which keeps the FIRST n) trims predictably.
        : SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS.filter(
            (key) => key === formatKey || current.includes(key)
          )
    );
  }
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
  const refreshPackageJob = packageJob.refresh;

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
  const hasResolvedToken = token.length > 0;

  // When a package generation finishes, surface the Package tab so the user immediately sees what the
  // conversion produced. Fires once per job; a later manual tab switch by the user is respected.
  const autoSwitchedPackageJobRef = useRef<string | null>(null);
  const userStartedPackageJobRef = useRef<string | null>(null);
  useEffect(() => {
    if (!shouldAutoSwitchToPackage({
      activeJobId: packageJobId,
      observedJobId: packageJob.observedJobId,
      userStartedJobId: userStartedPackageJobRef.current,
      alreadySwitchedJobId: autoSwitchedPackageJobRef.current,
      status: packageUiStatus,
    })) return;
    autoSwitchedPackageJobRef.current = packageJobId;
    setRightPanelTab("package");
  }, [packageJobId, packageJob.observedJobId, packageUiStatus]);
  const organizationSelectionReady = isPlatformAdmin
    ? organizationsInitialized && (organizations.length === 0 || selectedOrganizationId.length > 0)
    : activeOrgInitialized && (activeOrgId === null || selectedOrganizationId === activeOrgId);
  const clientsReady =
    selectedOrganizationId.length === 0 || clientsLoadedOrganizationId === selectedOrganizationId;
  const initialLoadStageKey = resolveInitialLoadStageKey({
    hasResolvedToken,
    organizationSelectionReady,
    clientsReady,
    networksLoading,
    historyLoaded,
  });
  const initialToolLoading = initialLoadStageKey !== null;

  // Load the server-driven format catalog once (best-effort; falls back to static union).
  useEffect(() => {
    if (!effectiveToken) return;
    let cancelled = false;
    listFormatCatalog({ token: effectiveToken })
      .then((entries) => { if (!cancelled) setFormatCatalogEntries(entries); })
      .catch(() => { /* non-fatal: static union is the fallback */ });
    return () => { cancelled = true; };
  }, [effectiveToken]);

  // Derive MutationMode from surface + layout whenever either changes (SCENARIO-12).
  useEffect(() => {
    if (mutationSurface === "script") {
      setMutationMode("script");
    } else if (mutationSurface === "story") {
      setMutationMode(mutationLayout === "carousel" ? "story-carousel" : "story-single");
    } else {
      // feed
      setMutationMode(mutationLayout === "carousel" ? "feed-carousel" : "feed-single");
    }
  }, [mutationSurface, mutationLayout]);

  // Shares the app-shell overlay via the store, in case another surface (e.g. route navigation)
  // is also holding it up; still rendered locally too (see the render guard below) so the tool
  // works standalone when the shell overlay isn't mounted around it.
  useEffect(() => {
    if (!initialLoadStageKey) {
      hideBlockingLoader(SINGLE_ANALYZER_BLOCKING_LOADER_ID);
      return;
    }

    return showBlockingLoader(SINGLE_ANALYZER_BLOCKING_LOADER_ID, {
      title: "Loading tool",
      stages: INITIAL_LOAD_STAGES,
      activeKey: initialLoadStageKey,
    });
  }, [initialLoadStageKey]);

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
          ? Object.keys(packageJob.manifest.formats)
              .map(packageManifestKeyToContentFormatKey)
              .filter((formatKey): formatKey is ContentFormatKey => formatKey !== null)
          : [];
      const fromClientManifest: ContentFormatKey[] =
        fromPerFormatStatus.length === 0 && isTerminalSuccess && packageJob.manifest?.clients
          ? Object.values(packageJob.manifest.clients)
              .flatMap((clientManifest) => Object.keys(clientManifest.formats ?? {}))
              .map(packageManifestKeyToContentFormatKey)
              .filter((formatKey): formatKey is ContentFormatKey => formatKey !== null)
          : [];
      return Array.from(new Set([...fromPerFormatStatus, ...fromManifest, ...fromClientManifest]));
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
        .map(packageManifestKeyToContentFormatKey)
        .filter((formatKey): formatKey is ContentFormatKey => formatKey !== null);
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

  // Surface a polling failure/timeout from the generation job so the overlay closing never leaves
  // the user without feedback (the client-side poll timeout is the last-resort exit from the spinner).
  useEffect(() => {
    if (packageJob.isError && packageJob.error) {
      setPackageJobError({ message: packageJob.error.message });
    }
  }, [packageJob.isError, packageJob.error]);

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
        ({ blob, filename } = await assemblePackage(
          analysisId,
          { jobId: packageJobId },
          { token: effectiveToken }
        ));
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
      void refreshPackageJob();
    } catch (e) {
      const err = e as { message?: string };
      setPackageJobError({ message: err?.message ?? "Failed to download package.zip." });
    } finally {
      setIsDownloadingPackage(false);
    }
  }, [packageJobId, isDownloadingPackage, effectiveToken, videoResult?.id, canAssembleFromStoredAssets, refreshPackageJob]);

  const downloadFormatAsPptxFile = useCallback(async (formatKey: string) => {
    if (isDownloadingPptx) return;
    const analysisId = videoResult?.id;
    const jobId = packageJobId;
    if (!analysisId || !jobId) return;
    setIsDownloadingPptx(true);
    setPackageJobError(null);
    try {
      const { blob, filename } = await downloadFormatAsPptx(analysisId, formatKey, jobId, {
        token: effectiveToken,
      });
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
      setPackageJobError({ message: err?.message ?? `Failed to download ${formatKey}.pptx.` });
    } finally {
      setIsDownloadingPptx(false);
    }
  }, [packageJobId, isDownloadingPptx, effectiveToken, videoResult?.id]);

  /**
   * Exports the given format to Canva via the Design Import API.
   * On success: opens the Canva edit URL in a new tab.
   * On not_connected / reconnect_required: redirects to the OAuth Connect flow.
   * On other errors: sets the package job error for the user to see.
   *
   * B3 WU-7 — mirrors downloadFormatAsPptxFile pattern.
   */
  const exportFormatToCanvaFile = useCallback(async (formatKey: string) => {
    if (isExportingToCanva) return;
    const analysisId = videoResult?.id;
    const jobId = packageJobId;
    if (!analysisId || !jobId) {
      setPackageJobError({
        message: "This format is not ready for Canva yet. Finish a successful generation first.",
      });
      return;
    }
    setIsExportingToCanva(true);
    setPackageJobError(null);
    try {
      // Lazy-load the org's Canva integration id so we don't need an extra prop.
      // Uses selectedOrganizationId which is already resolved from the active org context.
      const orgId = selectedOrganizationId;

      let orgIntegrationId = "";
      if (orgId) {
        try {
          const integration = await getOrganizationIntegration(orgId, "canva", effectiveToken ?? undefined);
          orgIntegrationId = integration.integrationId ?? "";
        } catch {
          // Integration not configured — fall through; the handler returns not_connected.
        }
      }

      const result = await exportFormatToCanva(analysisId, formatKey, jobId, orgIntegrationId, {
        token: effectiveToken,
      });
      // Success: open Canva design edit URL in a new tab
      window.open(result.editUrl, "_blank", "noopener,noreferrer");
    } catch (e) {
      const err = e as { message?: string; code?: string };
      const code = err.code ?? "";

      // Canva is connected at the ORG level by an admin (Settings → Integrations); individual users
      // are never redirected to OAuth. If the org isn't connected, say so clearly.
      if (code === "canva.not_connected" || code === "canva.reconnect_required") {
        setPackageJobError({
          message:
            "Canva isn't connected for this organization yet. An admin needs to connect it in Settings → Integrations.",
        });
        return;
      }

      setPackageJobError({
        message: err?.message ?? `Failed to export ${formatKey} to Canva.`,
      });
    } finally {
      setIsExportingToCanva(false);
    }
  }, [packageJobId, isExportingToCanva, effectiveToken, videoResult?.id, selectedOrganizationId]);

  /**
   * Sends the whole VISUAL package of the current job (story + carousel slides) to Canva as ONE
   * editable multi-page design, titled after the client. Mirrors exportFormatToCanvaFile's
   * connect-on-demand behavior. B3 — package-level Send to Canva.
   */
  const sendPackageToCanva = useCallback(async () => {
    if (isExportingToCanva) return;
    const analysisId = videoResult?.id;
    const jobId = packageJobId;
    if (!analysisId || !jobId) {
      setPackageJobError({
        message: "Package is not ready for Canva yet. Finish a successful generation first.",
      });
      return;
    }
    setIsExportingToCanva(true);
    setPackageJobError(null);
    try {
      const result = await exportPackageToCanva(analysisId, jobId, canvaOrgIntegrationId, {
        token: effectiveToken,
      });
      window.open(result.editUrl, "_blank", "noopener,noreferrer");
      void refreshPackageJob();
    } catch (e) {
      const err = e as { message?: string; code?: string };
      const code = err.code ?? "";
      // Canva is connected at the ORG level by an admin (Settings → Integrations). Individual users
      // are never redirected to OAuth — if the org isn't connected, we say so clearly.
      if (code === "canva.not_connected" || code === "canva.reconnect_required") {
        setPackageJobError({
          message:
            "Canva isn't connected for this organization yet. An admin needs to connect it in Settings → Integrations.",
        });
        return;
      }
      setPackageJobError({ message: err?.message ?? "Failed to send the package to Canva." });
    } finally {
      setIsExportingToCanva(false);
    }
  }, [packageJobId, isExportingToCanva, effectiveToken, videoResult?.id, canvaOrgIntegrationId, refreshPackageJob]);

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

  // Niche-search readiness is looked up as soon as the operator selects the niche criterion (and again
  // when they change network), so the banner states the TRUE reason instead of assuming "unconfigured"
  // before anything has been checked.
  useEffect(() => {
    if (discoveryCriterion !== "niche" || !selectedOrganizationId || !effectiveToken) {
      setNicheSearchReadiness(null);
      return;
    }

    let cancelled = false;

    void (async () => {
      try {
        const overview = await listOrganizationIntegrations(selectedOrganizationId, effectiveToken);
        if (!cancelled) {
          setNicheSearchReadiness(getNicheSearchReadiness(overview.checklistGroups, searchNetwork));
        }
      } catch {
        // Best-effort UX only: the discovery chain still degrades cleanly to an empty, non-throwing
        // result when the stage is unconfigured, so a failed lookup never blocks the operator.
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [discoveryCriterion, selectedOrganizationId, searchNetwork, effectiveToken]);

  // Derived — always in sync with API data
  const networkOptions = socialNetworks.map((n) => ({ value: n.code, label: n.displayName }));
  // Blocking reason for the niche stage, worded for the network the operator actually picked — never a
  // blanket "configure a TikTok actor" for a network that has no adapter at all.
  const nicheSearchBlockedMessage = getNicheSearchBlockedMessage(
    discoveryCriterion,
    nicheSearchReadiness ?? "ready",
    networkOptions.find((n) => n.value === searchNetwork)?.label
  );
  // Why a terminal discovery produced no shortlist — null while running and once candidates are in.
  const discoveryOutcomeMessage = discoveryOutcome ? getDiscoveryOutcomeMessage(discoveryOutcome) : null;
  const urlSupportedPlatformNames = useMemo(() => {
    const supportedCodes = new Set(["tiktok", "instagram", "youtube", "x", "facebook", "linkedin"]);
    const activeSupportedNames = socialNetworks
      .filter((network) => supportedCodes.has(network.code.toLowerCase()))
      .map((network) => network.displayName);
    return activeSupportedNames.length > 0
      ? activeSupportedNames
      : ["TikTok", "Instagram", "YouTube", "LinkedIn"];
  }, [socialNetworks]);
  const urlSupportedPlatformsLabel = useMemo(
    () => formatListForPlaceholder(urlSupportedPlatformNames),
    [urlSupportedPlatformNames]
  );
  const urlInputPlaceholder = `Paste a URL from ${urlSupportedPlatformsLabel}…`;
  const selectedClient = useMemo(
    () => clients.find((client) => client.id === selectedClientId) ?? null,
    [clients, selectedClientId]
  );
  const clientContextOptions = useMemo<OptionCardSelectorOption[]>(
    () =>
      clients.map((client) => ({
        value: client.id,
        label: client.name,
        description: client.name || client.id,
        icon: <Building2 className="h-4 w-4" />,
      })),
    [clients]
  );
  const hasClientContext = selectedClientId.trim().length > 0;
  const generationClientId = selectedClientId;
  const packageGenerationClientMissing = !generationClientId;
  // A saved analysis can be converted for multiple active clients; each generated output
  // is scoped by the client selected at generation time.

  // Normalize searchNetwork when networks load. TikTok is the default because it is the only network
  // with a working niche-search adapter — defaulting to whatever happened to be first in the catalog
  // put the operator on a network whose search cannot run.
  useEffect(() => {
    if (socialNetworks.length === 0) return;
    setSearchNetwork((prev) =>
      socialNetworks.some((n) => n.code === prev)
        ? prev
        : resolveDefaultSearchNetwork(socialNetworks.map((n) => n.code))
    );
  }, [socialNetworks]);

  const loadClients = useCallback(async () => {
    if (!selectedOrganizationId) {
      clientsRequestIdRef.current += 1;
      setClients([]);
      setSelectedClientId("");
      setClientsLoadedOrganizationId(null);
      return;
    }

    const requestId = ++clientsRequestIdRef.current;
    setClientsLoading(true);
    setClientsLoadedOrganizationId(null);
    try {
      const result = effectiveToken
        ? (await listWorkflowClients(selectedOrganizationId, effectiveToken)).items
        : getMockClients(selectedOrganizationId);
      if (requestId !== clientsRequestIdRef.current) {
        return;
      }
      const activeClients = result.filter((client) => client.isActive);
      setClients(activeClients);
      setSelectedClientId((current) =>
        current && activeClients.some((client) => client.id === current) ? current : ""
      );
    } catch {
      if (requestId !== clientsRequestIdRef.current) {
        return;
      }
      const activeClients = getMockClients(selectedOrganizationId).filter((client) => client.isActive);
      setClients(activeClients);
      setSelectedClientId((current) =>
        current && activeClients.some((client) => client.id === current) ? current : ""
      );
    } finally {
      if (requestId !== clientsRequestIdRef.current) {
        return;
      }
      setClientsLoadedOrganizationId(selectedOrganizationId);
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
    setOrganizationsInitialized(true);
  }, []);

  const loadHistory = useCallback(async (opts?: { silent?: boolean }) => {
    if (!effectiveToken) {
      historyRequestIdRef.current += 1;
      setHistoryLoading(false);
      setHistoryLoaded(false);
      return;
    }

    const requestId = opts?.silent ? historyRequestIdRef.current : ++historyRequestIdRef.current;
    if (!opts?.silent) {
      setHistoryLoading(true);
      setHistoryLoaded(false);
    }

    try {
      const result = await listSingleContentAnalyses(1, 20, effectiveToken);
      if (opts?.silent || requestId === historyRequestIdRef.current) {
        setHistoryItems(result.items);
      }
    } catch {
      // Silent: keep the previous list rather than wipe the grid on a transient blip. A failure
      // banner here would be noisy for a peripheral panel that doesn't block the main flow.
    } finally {
      if (!opts?.silent) {
        if (requestId !== historyRequestIdRef.current) {
          return;
        }
        setHistoryLoaded(true);
        setHistoryLoading(false);
      }
    }
  }, [effectiveToken]);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory, selectedOrganizationId]);

  // Detect whether the active org has the Canva connector configured. This gates the package-level
  // "Send to Canva" action: it appears only when the org integration exists. Per-user connection is
  // still handled reactively (clicking starts the OAuth flow when the user hasn't connected).
  useEffect(() => {
    let cancelled = false;
    async function checkCanvaConfigured() {
      if (!selectedOrganizationId) {
        setCanvaConfigured(false);
        setCanvaOrgIntegrationId("");
        return;
      }
      try {
        const integration = await getOrganizationIntegration(
          selectedOrganizationId,
          "canva",
          effectiveToken ?? undefined
        );
        const id = integration.integrationId ?? "";
        if (!cancelled) {
          setCanvaConfigured(Boolean(id));
          setCanvaOrgIntegrationId(id);
        }
      } catch {
        if (!cancelled) {
          setCanvaConfigured(false);
          setCanvaOrgIntegrationId("");
        }
      }
    }
    void checkCanvaConfigured();
    return () => {
      cancelled = true;
    };
  }, [selectedOrganizationId, effectiveToken]);

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
    setDiscoveryCandidates([]);
    setDiscoveryError(null);
    setDiscoveryOutcome(null);
    setDiscoverySteps([]);
    setDiscoveryMessage("");
    setDiscoveryStatusValue("in_progress");
    setCandidateSource(null);
    setNicheSearchReadiness(null);
    setSelectedClientId("");
    setDuplicateAnalysisPrompt(null);
    setRightPanelTab("conversion");
    setMutationMode("script");
    setMutationSurface("script");
    setMutationLayout("single");
    setPendingRegeneration(null);
    setPendingDeleteHistoryItem(null);
    setPendingNavigationHref(null);
    setIsAbandonDialogOpen(false);
    pollCancelRef.current = false;
  }

  async function exportCurrentAnalysisPdf() {
    if (!videoResult || !contentExportRef.current || isExportingPdf) return;
    setIsExportingPdf(true);
    try {
      await exportElementToPdf(contentExportRef.current, {
        filename: `${slugForFilename(videoResult.title)}-content-analysis.pdf`,
      });
      toast.success("Print dialog opened", "Use your browser's Save as PDF option to download the analysis.");
    } catch (e) {
      const err = e as { message?: string };
      setError({
        message: err?.message ?? "Failed to export this analysis as PDF.",
      });
    } finally {
      setIsExportingPdf(false);
    }
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

  async function copyPackageJobId() {
    if (!packageJobId) return;
    try {
      await navigator.clipboard.writeText(packageJobId);
      toast.success("Job ID copied", packageJobId);
    } catch {
      toast.error("Couldn't copy Job ID", "Copy it manually: " + packageJobId);
    }
  }

  async function loadVideoByUrl(opts?: { force?: boolean }) {
    // Reentrancy guard: Enter keydown + button click (or two rapid triggers before React
    // repaints `disabled`) can both call this function within the same tick. Bail out
    // immediately without touching any state if a request is already in flight.
    if (!canStartAnalyzeRequest(loadVideoInFlightRef.current)) return;
    loadVideoInFlightRef.current = true;
    try {
      const trimmedUrl = urlInput.trim();
      if (!trimmedUrl) return;
      const platform = detectPlatformFromUrl(trimmedUrl);
      if (!platform || platform === "unknown") {
        setDuplicateAnalysisPrompt(null);
        setError({
          code: "social.single_analyzer.invalid_url_input",
          message:
            `We can only analyze valid social URLs from ${urlSupportedPlatformsLabel}.`,
        });
        return;
      }

      // The overlay must appear the instant the user clicks Analyze, not after the duplicate-lookup
      // round-trip resolves. setIsLoading/setAnalysisStatus run synchronously here, before any await,
      // so <StageOverlay visible={analysisStatus !== null}> renders on this same tick. If the
      // lookup below finds a duplicate we revert both flags before showing the duplicate prompt
      // instead of the overlay.
      setDuplicateAnalysisPrompt(null);
      setIsLoading(true);
      setAnalysisStatus("Requested");

      if (!opts?.force && effectiveToken) {
        setIsCheckingDuplicate(true);
        setError(null);
        try {
          const duplicate = await findSingleContentAnalysisByUrl(trimmedUrl, effectiveToken);
          if (duplicate.item) {
            setDuplicateAnalysisPrompt(duplicate.item);
            setIsLoading(false);
            setAnalysisStatus(null);
            return;
          }
        } catch {
          // Do not block the user's explicit analysis request on a lookup-only failure.
        } finally {
          setIsCheckingDuplicate(false);
        }
      }

      resetVideoState();
      setIsSavedAnalysisView(false);
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

        if (
          code === "social.single_analyzer.analysis_already_in_progress" ||
          code === "social.single_analyzer.analysis_already_completed"
        ) {
          // The backend's HARD org-scoped duplicate gate rejected this before touching Apify —
          // the advisory pre-check above can miss this (another user in the org holds the record,
          // or a race let two requests through the pre-check at once). Best-effort: look up OUR
          // OWN saved analysis for this URL so the existing duplicate-prompt UI (Resume / Analyze
          // again) still drives the next step; if it's someone else's in-flight run we won't find
          // our own row, so fall back to a plain explanatory message instead.
          try {
            const duplicate = await findSingleContentAnalysisByUrl(trimmedUrl, effectiveToken);
            if (duplicate.item) {
              setDuplicateAnalysisPrompt(duplicate.item);
              return;
            }
          } catch {
            // Lookup-only failure — fall through to the generic message below.
          }

          setError({
            code,
            message:
              code === "social.single_analyzer.analysis_already_in_progress"
                ? "This video is already being analyzed (by you or a teammate in this organization). Wait for it to finish, then check Saved analyses."
                : "This video has already been analyzed. Check Saved analyses to view or resume it.",
          });
          return;
        }

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
    } finally {
      loadVideoInFlightRef.current = false;
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
        message: err?.message || "Retry failed. Please try again.",
      });
    } finally {
      setIsLoading(false);
      setAnalysisStatus(null);
    }
  }

  async function pollUntilTerminal(id: string) {
    const deadline = Date.now() + POLL_TIMEOUT_MS;
    let attemptIndex = 0;
    while (Date.now() < deadline) {
      if (pollCancelRef.current) return null;
      const delay = pollAttemptDelayMs(attemptIndex, POLL_INTERVAL_MS);
      if (delay > 0) {
        await sleep(delay);
        if (pollCancelRef.current) return null;
      }
      attemptIndex += 1;
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

  /**
   * Discovery shortlist — start + poll (WU-7, design D8/§8.2).
   *
   * `startContentWorkflow` now only OPENS the session: the scrape runs on the backend's own
   * background task, so this call returns in milliseconds and the browser or proxy can no longer
   * abort a 30-second request and orphan a billing Apify run. The outcome arrives through
   * `getContentWorkflowStatus`, polled on the same cadence and rendered through the same
   * screen-blocking overlay as the analysis flow. Results are fetched only once the session reports
   * a terminal state with candidates.
   */
  async function runDiscovery(options?: { forceRefresh?: boolean; originMode?: "discovery" | "ai-ideas" }) {
    if (!selectedClientId || isDiscovering) return;

    const originMode = options?.originMode ?? "discovery";
    const isAiIdeas = originMode === "ai-ideas";

    // A refresh is the ONLY way this screen can cause a billed provider run, and it is only offered
    // for the niche criterion. Re-checked here, not just on the button, so no future caller can hand
    // this function an opt-in the guard would have refused.
    //
    // The ai-ideas mode touches no provider at all, so a refresh opt-in there is meaningless and is
    // dropped rather than forwarded — the two must never be combined.
    const forceRefresh =
      !isAiIdeas &&
      options?.forceRefresh === true &&
      canStartRefresh({ criterion: discoveryCriterion, clientId: selectedClientId, isDiscovering });

    setDiscoveryError(null);
    setDiscoveryCandidates([]);
    setDiscoveryOutcome(null);
    setDiscoverySteps([]);
    setDiscoveryMessage("");
    setDiscoveryStatusValue("in_progress");
    setCandidateSource(null);

    // Skipped for ai-ideas: this checks whether an Apify niche actor is configured, and that run
    // never dispatches one. Blocking on it would refuse a mode built precisely for the case where no
    // actor is set up.
    if (discoveryCriterion === "niche" && !isAiIdeas) {
      try {
        const overview = await listOrganizationIntegrations(selectedOrganizationId, effectiveToken);
        const readiness = getNicheSearchReadiness(overview.checklistGroups, searchNetwork);
        setNicheSearchReadiness(readiness);
        if (isNicheSearchBlocked(discoveryCriterion, readiness)) {
          setDiscoveryOutcome("unconfigured");
          return;
        }
      } catch {
        // Checklist lookup is best-effort UX; an unconfigured stage is still reported as such by the
        // session's own terminal status, so we do not block on it.
      }
    }

    setIsDiscovering(true);
    try {
      const client = clients.find((c) => c.id === selectedClientId) ?? null;
      const networkSources = searchNetwork ? [searchNetwork] : networkOptions.map((n) => n.value);

      const session = await startContentWorkflow(
        selectedOrganizationId,
        {
          clientId: selectedClientId,
          clientName: client?.name ?? "",
          searchCriterion: discoveryCriterion,
          strategyMode: searchStrategy as ContentWorkflowStrategyMode,
          networkSources,
          forceRefresh,
          originMode,
        },
        effectiveToken
      );

      setDiscoveryMessage(session.progressMessage);

      const deadline = Date.now() + DISCOVERY_POLL_TIMEOUT_MS;
      let latest: ContentWorkflowStatus | null = null;

      while (Date.now() < deadline) {
        await sleep(DISCOVERY_POLL_INTERVAL_MS);
        latest = await getContentWorkflowStatus(
          selectedOrganizationId,
          session.workflowId,
          effectiveToken
        );
        setDiscoverySteps(latest.steps);
        setDiscoveryMessage(latest.progressMessage);
        setDiscoveryStatusValue(latest.status);

        if (isDiscoveryTerminal(latest.status)) break;
      }

      if (!latest || !isDiscoveryTerminal(latest.status)) {
        // The cap ran out with the session still working. Say exactly that — the run itself is not
        // cancelled and is still billing-bounded by the platform's own Apify guards.
        setDiscoveryError(
          "Discovery is taking longer than expected and is still running on the server. Try again in a moment."
        );
        return;
      }

      const outcome = resolveDiscoveryOutcome(latest);
      setDiscoveryOutcome(outcome);
      setCandidateSource(summariseCandidateSource(latest, new Date().toISOString()));

      if (outcome === "candidates") {
        const page = await listContentWorkflowResults(
          selectedOrganizationId,
          session.workflowId,
          1,
          24,
          effectiveToken
        );
        setDiscoveryCandidates(mapDiscoveryResultsToShortlistCandidates(page.items));
      }
    } catch (e) {
      const err = e as { message?: string };
      setDiscoveryError(err?.message || "Discovery failed. Try again.");
    } finally {
      setIsDiscovering(false);
    }
  }

  /**
   * The shortlist already POSTed exactly once to the EXISTING single-analysis start endpoint
   * (`analyzeSingleContent`) and hands us the returned analysis id here. From this point on it is
   * the SAME overlay/poll path `loadVideoByUrl` uses after its own `analyzeSingleContent` call —
   * intentionally duplicated rather than factored out, to avoid touching that already-working path.
   */
  async function handleDiscoveryAnalysisStarted(analysisId: string) {
    setDuplicateAnalysisPrompt(null);
    setIsLoading(true);
    resetVideoState();
    setIsSavedAnalysisView(false);
    setAnalysisStatus("Requested");
    pollCancelRef.current = false;
    try {
      const final = await pollUntilTerminal(analysisId);
      if (!final) return; // cancelled (component unmounted)

      if (final.status === "Failed") {
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

  function handleDiscoveryError(message: string) {
    setDiscoveryError(message);
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
    // MutationMode is now the resolved ContentFormatKey for the 5 active formats,
    // except "script" → "reel-script" (surface name differs from the API key).
    if (mode === "script") return "reel-script";
    return mode as ContentFormatKey;
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
    // Drive the blocking generation overlay from the moment the request starts, matching
    // startRequestedFormatsGeneration. Without this, a single-format (re)generation — including the
    // "with objections" path — leaves packageGenerationIsRunning false during the requestPackage
    // await (packageJobId is nulled below and no job is polling yet), so the process modal never
    // shows for that window.
    setIsRequestingPackage(true);
    const formatKey = formatForMutationMode(mode);
    setPackageJobError(null);
    userStartedPackageJobRef.current = null;
    setPackageJobId(null);
    // Set the requested-format count BEFORE the await so the progress overlay can never show a
    // stale denominator (e.g. "(1/3)") for a single-format job — it must read 1 from the moment
    // the overlay can appear. A story is one format = one file; >1 only applies to a carousel.
    setPackageRequestedFormats([formatKey]);
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
      userStartedPackageJobRef.current = response.jobId;
      setPackageJobId(response.jobId);
      setPackageJobClientId(selectedClientId || null);
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
      // Hand off to job polling (isLoading/isPolling/status keep the overlay up once packageJobId
      // is set); on failure this clears the overlay so the screen unblocks.
      setIsRequestingPackage(false);
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

  /**
   * Confirms the pending package deletion (a format folder or a single file/slide).
   * Deletes from storage + DB on the backend, then re-fetches the package tree so the
   * UI reflects the cascade (an emptied folder disappears automatically).
   */
  async function confirmPackageDelete() {
    const target = pendingPackageDelete;
    const analysisId = videoResult?.id;
    if (!target || !analysisId) {
      setPendingPackageDelete(null);
      return;
    }
    setIsFormatMutating(true);
    setPackageJobError(null);
    try {
      if (target.kind === "format") {
        await deleteFormatItem(analysisId, target.formatKey, target.clientId, {
          token: effectiveToken,
        });
        markFormatDeleted(target.clientId, target.formatKey);
      } else {
        await deleteAsset(analysisId, target.formatKey, target.clientId, target.index, {
          token: effectiveToken,
        });
      }
      refreshPackages();
    } catch (e) {
      const err = e as { message?: string };
      setPackageJobError({ message: err?.message ?? "Failed to delete from package." });
    } finally {
      setIsFormatMutating(false);
      setPendingPackageDelete(null);
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
  // "Regenerate all formats" is offered ONLY when the user actually ran an all-formats generation
  // (a single all-formats job that has finished — completed or partial). Generating formats one by
  // one — e.g. just a story — must NOT enable all-formats regeneration, even if every format happens
  // to exist from separate single-format runs.
  const shouldRegenerateAllFormats =
    packageGenerationIsAllFormatJob &&
    (packageGenerationIsCompleted || packageGenerationIsPartial);
  const convertAllPills = SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS
    .filter((format) => {
      if (generatedFormatKeys.includes(format)) return true;
      return Boolean(currentPackageJobMatchesSelectedClient && packageJob.perFormatStatus[format]);
    })
    .flatMap((format) => {
      const liveStatus = currentPackageJobMatchesSelectedClient
        ? packageJob.perFormatStatus[format]
        : null;
      if (generatedFormatKeys.includes(format) && liveStatus === "Failed") {
        return [
          { format, status: "Generated" },
          { format, status: "Regeneration failed" },
        ];
      }
      const status = liveStatus ?? "Generated";
      return [{ format, status }];
    });
  // INC-6: hasAnyGeneratedFormat also checks the persisted multi-client workspace.
  const hasAnyGeneratedFormat =
    generatedFormatKeys.length > 0 ||
    (selectedClientId ? hasStoredFormatsForClient(selectedClientId) : false) ||
    hasAnyStoredGeneratedFormat;
  const canDownloadPackage = hasAnyGeneratedFormat && packageDownloadAvailable;
  const selectedFormatKey = formatForMutationMode();
  const getFormatFailureCount = useCallback(
    (formatKey: ContentFormatKey) => packageJob.perFormatFailureCounts[formatKey] ?? 0,
    [packageJob.perFormatFailureCounts]
  );
  const getBlockedFormats = useCallback(
    (formats: ContentFormatKey[]) =>
      formats.filter((format) => getFormatFailureCount(format) >= MAX_FORMAT_FAILURE_ATTEMPTS),
    [getFormatFailureCount]
  );
  const getRegenerationBlockedHint = useCallback(
    (formats: ContentFormatKey[]) => {
      const blocked = getBlockedFormats(formats);
      if (blocked.length === 0) return null;

      const labels = blocked.map((format) => FORMAT_LABELS[format]).join(", ");
      return `${labels} reached ${MAX_FORMAT_FAILURE_ATTEMPTS} failed generation attempts. Regeneration is blocked to prevent burning AI tokens; contact support before trying again.`;
    },
    [getBlockedFormats]
  );
  const selectedFormatRegenerationBlockedHint =
    getRegenerationBlockedHint([selectedFormatKey]);
  const allFormatsRegenerationBlockedHint =
    getRegenerationBlockedHint(SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS);

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
    const blockedHint = getRegenerationBlockedHint([formatKey]);
    if (blockedHint) {
      setPackageJobError({ message: blockedHint });
      return;
    }
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
      userStartedPackageJobRef.current = response.jobId;
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
    await startRequestedFormatsGeneration(selectedFormatKeys, regenerationObjection);
  }

  async function handleConvertAll(includeObjection = false) {
    if (!videoResult || isRequestingPackage || selectedFormatKeys.length === 0) return;
    const blockedHint = getRegenerationBlockedHint(selectedFormatKeys);
    if (blockedHint) {
      setPackageJobError({ message: blockedHint });
      return;
    }
    const currentObjection = resolveRegenerationObjection(includeObjection);
    if (includeObjection && !currentObjection) return;
    const existingFormats = getExistingGeneratedFormats(selectedFormatKeys);
    if (existingFormats.length > 0) {
      setPendingRegeneration({
        kind: "all",
        formats: selectedFormatKeys,
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
    if (packageJob.observedJobId !== packageJobId) return;

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
            hasPackage: hasPackage || previous?.hasPackage === true,
            packageJobId: hasPackage ? packageJobId : previous?.packageJobId ?? null,
            completedAt,
          },
        };
      })
    );
  }, [videoResult?.id, packageJobId, packageJob.observedJobId, packageUiStatus]);

  // On terminal transition, refetch once to keep row metadata (timestamps/package flags) exact.
  useEffect(() => {
    if (!shouldRefreshTerminalHistory({
      activeJobId: packageJobId,
      observedJobId: packageJob.observedJobId,
      status: packageUiStatus,
      lastRefreshKey: lastHistoryRefreshKeyRef.current,
    })) return;

    const refreshKey = `${packageJobId}:${packageUiStatus}`;
    lastHistoryRefreshKeyRef.current = refreshKey;
    void loadHistory({ silent: true });
  }, [packageJobId, packageJob.observedJobId, packageUiStatus, loadHistory]);

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
  const selectedFormatRegenerationTarget: RegenerationTarget = {
    kind: "single",
    mode: mutationMode,
    formatKey: selectedFormatKey,
  };
  const allFormatsRegenerationTarget: RegenerationTarget = { kind: "all" };
  const hasExistingFormatForCurrentMode =
    getExistingGeneratedFormats([selectedFormatKey]).length > 0;
  const shouldShowSavedAnalysisActions = isAnalyzed && Boolean(videoResult);
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
  const aiAnalysisFailedAttemptCount = videoResult?.aiAnalysisFailedAttemptCount ?? 0;
  const isAiAnalysisRetryBlocked =
    Boolean(videoResult?.isAiAnalysisRetryBlocked) ||
    aiAnalysisFailedAttemptCount >= MAX_AI_ANALYSIS_FAILURE_ATTEMPTS;
  const aiAnalysisRetryBlockedHint =
    `AI analysis reached ${MAX_AI_ANALYSIS_FAILURE_ATTEMPTS} failed attempts. ` +
    "Retry is blocked to prevent burning AI credits; contact support and we'll inspect the blocker.";

  const handleVideoThumbnailError = useCallback((event: SyntheticEvent<HTMLImageElement>) => {
    if (event.currentTarget.src.includes(FALLBACK_THUMBNAIL_PATH)) return;
    event.currentTarget.src = FALLBACK_THUMBNAIL_PATH;
  }, []);

  // ── Render ─────────────────────────────────────────────────────────────────

  const discoveryActiveIndex = discoveryStepIndex(discoverySteps, discoveryStatusValue);
  const discoveryActiveKey =
    discoverySteps[Math.min(discoveryActiveIndex, discoverySteps.length - 1)]?.key ?? null;

  return (
    <>
      {/* Full-screen initial-load stage overlay: real stages, no return-null screen cut. The tool
          tree below fades in once it mounts instead of popping in abruptly (issue: blocking-loader
          overlays unification). */}
      <StageOverlay
        visible={initialToolLoading}
        stages={INITIAL_LOAD_STAGES}
        activeKey={initialLoadStageKey}
        runningTitle="Loading tool"
        description="Loading organizations, clients, networks and saved analyses..."
        ariaLabel="Loading tool"
      />
      <AnimatePresence>
        {!initialToolLoading ? (
          <motion.div
            key="single-analyzer-content"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2, ease: "easeOut" }}
          >
    <div ref={contentExportRef} className="flex flex-col gap-6 p-6 max-w-screen-xl mx-auto">
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
        <div className="flex items-center gap-2" data-pdf-exclude="true">
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
            // A failed analysis has nothing to Export, but the user still needs an easy way to
            // start over next to Help instead of hunting for the URL input — show New whenever
            // there's a completed OR failed analysis on screen (error is always set on Failed,
            // both for a live run and one reopened from history).
            showNewAction={shouldShowSavedAnalysisActions || Boolean(error)}
            onHelp={openAnalyzerHelp}
            onSave={() => void exportCurrentAnalysisPdf()}
            onNew={startNewAnalysis}
            actionsDisabled={isLoading || packageGenerationIsRunning || isExportingPdf}
            saveLabel={isExportingPdf ? "Exporting…" : "Export"}
            newLabel="New analysis"
            jobId={packageJobId}
            onCopyJobId={() => void copyPackageJobId()}
          />
        </div>
      </div>

      {/* Error banner with built-in support actions */}
      <div data-pdf-exclude="true">
        <SupportError
          error={error}
          context={{ feature: "single-analyzer", organizationId: selectedOrganizationId }}
          onDismiss={() => setError(null)}
        />
      </div>

      {/* Action-level blocker for reading a saved analysis after the tool itself is already bootstrapped. */}
      <StageOverlay
        visible={isHydratingHistory}
        stages={[{ key: "hydrate", label: "Loading saved analysis" }]}
        runningTitle="Loading saved analysis"
        description="Recovering snapshot and analysis from history..."
      />
      <StageOverlay
        visible={analysisStatus !== null}
        stages={ANALYSIS_STEPS}
        activeKey={statusToStageKey(analysisStatus)}
        outcome={analysisStatus === "Analyzed" ? "done" : analysisStatus === "Failed" ? "failed" : "running"}
        runningTitle="Analyzing content"
        doneTitle="Analysis complete"
        failedTitle="Analysis failed"
        description="This usually takes 30–60 seconds. Keep this tab open."
        ariaLabel="Analyzing content"
      />
      {/* Discovery runs in the background on the server and is polled here, so the operator gets the
          SAME screen-blocking stage overlay as an analysis — the stage rows come straight from the
          session's own status response rather than from a second, invented progress model. */}
      <StageOverlay
        visible={isDiscovering}
        stages={discoverySteps.map((step) => ({ key: step.key, label: step.label }))}
        activeKey={discoveryActiveKey}
        outcome={
          discoveryStatusValue === "completed"
            ? "done"
            : discoveryStatusValue === "failed" || discoveryStatusValue === "unconfigured"
              ? "failed"
              : "running"
        }
        runningTitle="Discovering content"
        doneTitle="Discovery complete"
        failedTitle="Discovery failed"
        description={discoveryMessage || "Scraping runs on the server. You can keep this tab open."}
        ariaLabel="Discovering content"
      />
      <PackageGenerationOverlay
        visible={packageGenerationIsRunning}
        status={packageUiStatus}
        perFormatStatus={packageJob.perFormatStatus}
        totalFormats={packageRequestedFormats.length}
      />

      {/* Main 2-col grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6" data-pdf-main-grid>
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
                    <div className="grid grid-cols-[minmax(0,1fr)_auto] gap-2">
                      <Input
                        placeholder={urlInputPlaceholder}
                        value={urlInput}
                        onChange={(e) => setUrlInput(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter" && !(isLoading || isCheckingDuplicate || !canAnalyzeByUrl)) {
                            void loadVideoByUrl();
                          }
                        }}
                        className="h-8 min-w-0 text-xs"
                      />
                      <Button
                        size="sm"
                        onClick={() => void loadVideoByUrl()}
                        disabled={isLoading || isCheckingDuplicate || !canAnalyzeByUrl}
                        className="h-8 shrink-0 px-3 text-xs"
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
                    {/* Discovery shortlist (WU-7, design D8/§8.2): runs the real discovery chain for
                        the selected client + criterion through the EXISTING content-workflow
                        endpoints; the operator selects EXACTLY ONE candidate for instant analysis
                        through the EXISTING single-analysis start+poll path (no new backend transport). */}
                    <div className="flex gap-2">
                      <Select
                        size="sm"
                        className="w-40"
                        value={selectedClientId}
                        onChange={(e) => changeClientContext(e.target.value)}
                        disabled={clientsLoading || clients.length === 0}
                      >
                        <option value="">Select a client…</option>
                        {clients.map((c) => (
                          <option key={c.id} value={c.id}>{c.name}</option>
                        ))}
                      </Select>
                      <Select
                        size="sm"
                        className="w-36"
                        value={discoveryCriterion}
                        onChange={(e) =>
                          setDiscoveryCriterion(e.target.value as "references" | "own" | "niche")
                        }
                      >
                        {DISCOVERY_CRITERION_OPTIONS.map((c) => (
                          <option key={c.value} value={c.value}>{c.label}</option>
                        ))}
                      </Select>
                    </div>
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
                      <Button
                        size="sm"
                        onClick={() => void runDiscovery()}
                        disabled={isDiscovering || !selectedClientId}
                        className="h-8 px-3 text-xs"
                        title="Serves the candidates already stored for this niche. Nothing is scraped and nothing is billed unless the store is empty."
                      >
                        {isDiscovering ? <RefreshCw className="h-3 w-3 animate-spin" /> : <Search className="h-3 w-3" />}
                        Discover
                      </Button>
                      {/* Deliberately NOT a second Discover: this is the only control on the screen
                          that can spend money, so it is styled apart, labelled with the cost, and
                          disabled with a stated reason rather than silently. */}
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => void runDiscovery({ forceRefresh: true })}
                        disabled={
                          !canStartRefresh({
                            criterion: discoveryCriterion,
                            clientId: selectedClientId,
                            isDiscovering,
                          })
                        }
                        className="h-8 px-3 text-xs"
                        title={REFRESH_BILLING_WARNING}
                      >
                        <RefreshCw className="h-3 w-3" />
                        Refresh (billed run)
                      </Button>
                    </div>
                    {/* A different KIND of run, not a third way to discover: no actor is dispatched
                        and no candidate bank is read, so the topics are whatever the model comes up
                        with for this client at this moment. The client's aesthetic is untouched —
                        it is applied downstream at generation, exactly as for a scraped run. */}
                    <div className="flex gap-2">
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => void runDiscovery({ originMode: "ai-ideas" })}
                        disabled={isDiscovering || !selectedClientId}
                        className="h-8 px-3 text-xs"
                        title="Spontaneous topics written by the model from this client's own context. No scraping, no actor, nothing billed — and a different set on every run."
                      >
                        <Sparkles className="h-3 w-3" />
                        AI ideas (no scraping)
                      </Button>
                    </div>

                    {!selectedClientId && (
                      <p className="text-xs text-muted-foreground/70">
                        Select a client to discover candidates for.
                      </p>
                    )}

                    {/* The consequence is stated before the click, not after it: the whole
                        serve-from-store model rests on the operator opting in knowingly. */}
                    {selectedClientId && discoveryCriterion === "niche" && (
                      <p className="text-xs text-muted-foreground/70">{REFRESH_BILLING_WARNING}</p>
                    )}

                    {selectedClientId && discoveryCriterion !== "niche" && (
                      <p className="text-xs text-muted-foreground/70">
                        {getRefreshBlockedReason({
                          criterion: discoveryCriterion,
                          clientId: selectedClientId,
                          isDiscovering,
                        })}
                      </p>
                    )}

                    {discoveryError && (
                      <p className="text-xs text-destructive">{discoveryError}</p>
                    )}

                    {nicheSearchBlockedMessage && (
                      <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 space-y-2">
                        <div className="flex items-start gap-2">
                          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
                          <p className="text-xs text-foreground">{nicheSearchBlockedMessage}</p>
                        </div>
                        {nicheSearchReadiness === "unconfigured" && (
                          <Button
                            size="sm"
                            variant="outline"
                            className="h-8 text-xs"
                            onClick={() => router.push("/settings/integrations")}
                          >
                            Go to Settings / Integrations
                          </Button>
                        )}
                      </div>
                    )}

                    {/* A terminal outcome that produced nothing is explained by WHY it produced
                        nothing. A provider failure and an unconfigured stage must never render as
                        "No candidates found yet" — they have different fixes. */}
                    {!videoResult &&
                      !nicheSearchBlockedMessage &&
                      discoveryOutcomeMessage && (
                        <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 space-y-2">
                          <div className="flex items-start gap-2">
                            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
                            <p className="text-xs text-foreground">{discoveryOutcomeMessage}</p>
                          </div>
                          {discoveryOutcome && discoveryOutcomeOffersSettingsLink(discoveryOutcome) && (
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-8 text-xs"
                              onClick={() => router.push("/settings/integrations")}
                            >
                              Go to Settings / Integrations
                            </Button>
                          )}
                        </div>
                      )}

                    {/* Where these candidates came from, and how old the stored ones are. Rendered
                        only when the backend actually reported an origin — an unknown provenance is
                        left blank rather than guessed as fresh. */}
                    {!videoResult &&
                      discoveryOutcome === "candidates" &&
                      !nicheSearchBlockedMessage &&
                      candidateSource && (
                        <div
                          data-testid="candidate-source-indicator"
                          className={`flex items-start gap-2 rounded-lg border p-3 ${
                            candidateSource.origin === "bank"
                              ? "border-border/60 bg-muted/30"
                              : "border-primary/30 bg-primary/5"
                          }`}
                        >
                          {candidateSource.origin === "bank" ? (
                            <Database className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
                          ) : (
                            <Search className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                          )}
                          <div className="space-y-0.5">
                            <p className="text-xs font-medium text-foreground">{candidateSource.label}</p>
                            <p className="text-xs text-muted-foreground">{candidateSource.detail}</p>
                          </div>
                        </div>
                      )}

                    {!videoResult &&
                      discoveryOutcome === "candidates" &&
                      !nicheSearchBlockedMessage && (
                        <ContentDiscoveryShortlist
                          candidates={discoveryCandidates}
                          clientId={selectedClientId || undefined}
                          token={effectiveToken}
                          onAnalysisStarted={(analysisId) => void handleDiscoveryAnalysisStarted(analysisId)}
                          onError={handleDiscoveryError}
                        />
                      )}
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
                  <div className="space-y-1">
                    <Button
                      variant="outline"
                      className="w-full h-8 text-xs mt-1"
                      onClick={() => void retryAiAnalysis(videoResult.id)}
                      disabled={isLoading || isAiAnalysisRetryBlocked}
                      title={isAiAnalysisRetryBlocked ? aiAnalysisRetryBlockedHint : undefined}
                    >
                      <RefreshCw className="h-3 w-3" />
                      Retry AI analysis
                    </Button>
                    {isAiAnalysisRetryBlocked && (
                      <p className="text-[11px] leading-relaxed text-amber-600 dark:text-amber-300">
                        {aiAnalysisRetryBlockedHint}
                      </p>
                    )}
                  </div>
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
            <Card className="border-border/40 bg-background/60" data-pdf-exclude="true">
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
        <div className="flex flex-col gap-4" data-pdf-exclude="true">
          {isAnalyzed ? (
            <Card className="border-border/40 bg-background/60 flex-1">
                <CardHeader className="pb-3 space-y-3">
                <TabBar
                  tabs={[
                    { key: "conversion", label: "Convert" },
                    { key: "package", label: "Package" },
                  ]}
                  active={rightPanelTab}
                  onChange={(k) => setRightPanelTab(k as "conversion" | "package")}
                />
                <Button
                  size="sm"
                  className="mt-2 w-full"
                  disabled={!packageJobId || selectedClientId.length === 0}
                  onClick={() => setStudioOpen(true)}
                >
                  Open Content Studio
                </Button>
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
                              Generate the active formats on this screen: script, carousel, story, and feed post.
                              Download the <code className="text-[10px]">package.zip</code> when ready.
                            </p>
                          </div>
                        </div>
                        <div className="mb-2 flex flex-wrap gap-1.5">
                          {SINGLE_ANALYZER_ACTIVE_FORMAT_KEYS.map((formatKey) => {
                            const checked = selectedFormatKeys.includes(formatKey);
                            return (
                              <button
                                key={formatKey}
                                type="button"
                                disabled={packageGenerationIsRunning}
                                onClick={() => toggleFormatKey(formatKey)}
                                className={
                                  "rounded-full border px-2 py-0.5 text-[11px] transition-colors disabled:cursor-not-allowed disabled:opacity-50 " +
                                  (checked
                                    ? "border-primary bg-primary/10 text-primary"
                                    : "border-border text-muted-foreground hover:bg-muted")
                                }
                              >
                                {formatKey}
                              </button>
                            );
                          })}
                        </div>
                        {selectedFormatKeys.length === 0 ? (
                          <p className="mb-2 text-[11px] text-amber-600 dark:text-amber-400">
                            Pick at least one format to generate.
                          </p>
                        ) : null}

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
                                packageGenerationClientMissing ||
                                selectedFormatKeys.length === 0 ||
                                Boolean(allFormatsRegenerationBlockedHint)
                              }
                            title={
                              allFormatsRegenerationBlockedHint
                                ? allFormatsRegenerationBlockedHint
                                : shouldRegenerateAllFormats
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
                            ) : (
                              // On failure/partial we do NOT frame the button as "retry" — a blind
                              // retry just re-spends AI credits. The SupportError + PartialCompletionBanner
                              // below convey the state; this stays the neutral generate control.
                              <span className="flex items-center gap-2">
                                <Sparkles className="h-3 w-3" />
                                Convert to all formats
                              </span>
                            )}
                          </Button>
                        </div>
                        {allFormatsRegenerationBlockedHint && (
                          <p className="text-[10px] leading-relaxed text-destructive">
                            {allFormatsRegenerationBlockedHint}
                          </p>
                        )}
                        {isAllFormatsChoiceOpen && (
                          <RegenerationChoicePanel
                            onRegenerate={() => startSimpleRegeneration(allFormatsRegenerationTarget)}
                            onWithObjections={() => showRegenerationObjection(allFormatsRegenerationTarget)}
                            disabled={
                              packageGenerationIsRunning ||
                              packageGenerationClientMissing
                            }
                            blockedHint={allFormatsRegenerationBlockedHint}
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
                                key={`${fmt}-${st}`}
                                className={cn(
                                  "inline-flex items-center rounded px-1.5 py-0.5 text-[10px] border",
                                  st === "Rendered" || st === "Succeeded" || st === "Generated"
                                    ? "bg-green-500/10 text-green-600 dark:text-green-400 border-green-500/20"
                                    : st === "Failed" || st === "Regeneration failed"
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
                        <SupportError
                          error={{ message: packageJobError.message }}
                          context={{
                            feature: "single-analyzer",
                            organizationId: selectedOrganizationId,
                            details: {
                              analysisId: videoResult?.id ?? null,
                              scope: "content-generation",
                            },
                          }}
                          onDismiss={() => setPackageJobError(null)}
                        />
                      )}

                      {/* A FAILED generation routes the user to support instead of nudging a blind,
                          token-burning retry. The backend status_reason carries the real cause
                          (e.g. an off-palette guardrail block) so the copy is actionable and the
                          support report includes the technical detail. */}
                      {packageGenerationIsFailed && !packageJobError && (
                        <SupportError
                          error={resolveGenerationFailureError(packageJob.statusReason)}
                          context={{
                            feature: "single-analyzer",
                            organizationId: selectedOrganizationId,
                            details: {
                              analysisId: videoResult?.id ?? null,
                              scope: "content-generation",
                              clientId: packageJobClientId ?? selectedClientId ?? null,
                              statusReason: packageJob.statusReason ?? null,
                            },
                          }}
                        />
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
                      <div className="flex flex-wrap gap-1.5">
                        {/* 3 root surface buttons (Script / Story / Feed) — SCENARIO-12 */}
                        {(["script", "story", "feed"] as const).map((surface) => (
                          <button
                            key={surface}
                            type="button"
                            onClick={() => setMutationSurface(surface)}
                            className={cn(
                              "flex items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-medium border transition-colors capitalize",
                              mutationSurface === surface
                                ? "border-purple-500/50 bg-purple-500/10 text-purple-400"
                                : "border-border/40 text-muted-foreground hover:text-foreground"
                            )}
                          >
                            {surface === "script" && <Film className="h-3 w-3" />}
                            {surface === "story" && <Megaphone className="h-3 w-3" />}
                            {surface === "feed" && <FileImage className="h-3 w-3" />}
                            {surface === "script" ? "Script" : surface === "story" ? "Story" : "Feed"}
                          </button>
                        ))}
                        {/* Single ↔ Carousel layout toggle for Story and Feed (Script has none) */}
                        {mutationSurface !== "script" && (
                          <div className="flex gap-1 ml-1">
                            {(["single", "carousel"] as const).map((layout) => (
                              <button
                                key={layout}
                                type="button"
                                onClick={() => setMutationLayout(layout)}
                                className={cn(
                                  "flex items-center gap-1 rounded px-2 py-1 text-[10px] font-medium border transition-colors",
                                  mutationLayout === layout
                                    ? "border-purple-400/60 bg-purple-500/10 text-purple-300"
                                    : "border-border/30 text-muted-foreground/70 hover:text-foreground"
                                )}
                              >
                                {layout === "single" ? <ImageIcon className="h-2.5 w-2.5" /> : <ImageIcon className="h-2.5 w-2.5" />}
                                {layout === "single" ? "Single" : "Carousel"}
                              </button>
                            ))}
                          </div>
                        )}
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
                                  packageGenerationClientMissing ||
                                  Boolean(selectedFormatRegenerationBlockedHint)
                                }
                                title={selectedFormatRegenerationBlockedHint ?? undefined}
                              >
                                <RefreshCw className="h-3 w-3" />
                                Regenerate {mutationMode}
                              </Button>
                              {selectedFormatRegenerationBlockedHint && (
                                <p className="text-[10px] leading-relaxed text-destructive">
                                  {selectedFormatRegenerationBlockedHint}
                                </p>
                              )}
                              {isSelectedFormatChoiceOpen && (
                                <RegenerationChoicePanel
                                  onRegenerate={() => startSimpleRegeneration(selectedFormatRegenerationTarget)}
                                  onWithObjections={() => showRegenerationObjection(selectedFormatRegenerationTarget)}
                                  disabled={
                                    generatingConversion !== null ||
                                    packageGenerationIsRunning ||
                                    packageGenerationClientMissing
                                  }
                                  blockedHint={selectedFormatRegenerationBlockedHint}
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
                              {packageJob.manifest?.has_stored_assets &&
                                packageSucceededFormats.includes(selectedFormatKey) &&
                                mutationSurface !== "script" &&
                                packageJobId && (
                                  <Button
                                    size="sm"
                                    variant="outline"
                                    className="h-8 text-xs"
                                    onClick={() => void downloadFormatAsPptxFile(selectedFormatKey)}
                                    disabled={
                                      isDownloadingPptx ||
                                      isExportingToCanva ||
                                      isFormatMutating ||
                                      packageGenerationIsRunning
                                    }
                                    title="Download this format as an editable PowerPoint presentation."
                                  >
                                    {isDownloadingPptx ? (
                                      <span className="flex items-center gap-1">
                                        <RefreshCw className="h-3 w-3 animate-spin" />
                                        Downloading…
                                      </span>
                                    ) : (
                                      <span className="flex items-center gap-1">
                                        <Download className="h-3 w-3" />
                                        Download PPTX
                                      </span>
                                    )}
                                  </Button>
                                )}
                              {/* B3 WU-7 — Export to Canva (visual formats only) */}
                              {packageJob.manifest?.has_stored_assets &&
                                packageSucceededFormats.includes(selectedFormatKey) &&
                                mutationSurface !== "script" &&
                                packageJobId && (
                                  <Button
                                    size="sm"
                                    variant="outline"
                                    className="h-8 text-xs"
                                    onClick={() => void exportFormatToCanvaFile(selectedFormatKey)}
                                    disabled={
                                      isExportingToCanva ||
                                      isDownloadingPptx ||
                                      isFormatMutating ||
                                      packageGenerationIsRunning
                                    }
                                    title="Export this format to Canva as an editable design. Requires Canva connection."
                                  >
                                    {isExportingToCanva ? (
                                      <span className="flex items-center gap-1">
                                        <RefreshCw className="h-3 w-3 animate-spin" />
                                        Exporting…
                                      </span>
                                    ) : (
                                      <span className="flex items-center gap-1">
                                        <ExternalLink className="h-3 w-3" />
                                        Export to Canva
                                      </span>
                                    )}
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
                                packageGenerationClientMissing ||
                                Boolean(selectedFormatRegenerationBlockedHint)
                              }
                              title={
                                selectedFormatRegenerationBlockedHint ??
                                (hasExistingFormatForCurrentMode
                                  ? `Regenerate ${mutationMode}. The latest generated content will be replaced after confirmation.`
                                  : undefined)
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
                            {selectedFormatRegenerationBlockedHint && (
                              <p className="text-[10px] leading-relaxed text-destructive">
                                {selectedFormatRegenerationBlockedHint}
                              </p>
                            )}
                            {hasExistingFormatForCurrentMode && isSelectedFormatChoiceOpen && (
                              <RegenerationChoicePanel
                                onRegenerate={() => startSimpleRegeneration(selectedFormatRegenerationTarget)}
                                onWithObjections={() => showRegenerationObjection(selectedFormatRegenerationTarget)}
                                disabled={
                                  generatingConversion !== null ||
                                  packageGenerationIsRunning ||
                                  packageGenerationClientMissing
                                }
                                blockedHint={selectedFormatRegenerationBlockedHint}
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
                        ? selectedClient?.name ?? ""
                        : null)
                    }
                    clientPackages={clientPackages}
                    clientLabels={Object.fromEntries(
                      clients.map((c) => [c.id, c.name])
                    )}
                    actionState={packageJob.packageActionState}
                    onDownload={() => void handlePackageAction()}
                    showSendToCanva={canvaConfigured}
                    canSendToCanva={canDownloadPackage && packageJobId !== null}
                    isSendingToCanva={isExportingToCanva}
                    onSendToCanva={() => void sendPackageToCanva()}
                    onDeleteFormat={(clientId, formatKey, label) =>
                      setPendingPackageDelete({ kind: "format", clientId, formatKey, label })
                    }
                    onDeleteAsset={(clientId, formatKey, index, filename) =>
                      setPendingPackageDelete({ kind: "asset", clientId, formatKey, index, filename })
                    }
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

      {/* Batch generation — order N pieces at once and let the worker grind while the operator
          moves on. Deliberately a sibling of the analysis grid rather than a control inside it:
          the single-format buttons above act on THIS screen, this one hands work to the background
          and is tracked by the global indicator in the app shell. */}
      <div data-pdf-exclude="true">
        <BatchGenerationPanel
          analysisId={videoResult?.id ?? null}
          clientId={selectedClientId || null}
          disabled={generatingConversion !== null || packageGenerationIsRunning}
        />
      </div>

      {/* History — saved analyses owned by the current user. Click to reload everything as if
          the run just finished (snapshot + analysis + any existing content-generation job). */}
      {historyItems.length > 0 && (
        <Card className="border-border/40 bg-background/60" data-pdf-exclude="true">
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
                    item.latestContentGeneration?.packageJobId &&
                    void handleHistoryDownload(item.latestContentGeneration.packageJobId)
                  }
                  onDelete={() => requestDeleteFromHistory(item)}
                />
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {studioOpen && (
        <ContentStudioPanel
          analysisId={videoResult?.id ?? null}
          clientId={selectedClientId || null}
          token={token}
          onClose={() => setStudioOpen(false)}
        />
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
        open={pendingPackageDelete !== null}
        title={
          pendingPackageDelete?.kind === "format"
            ? "Delete this folder?"
            : "Delete this file?"
        }
        description={
          pendingPackageDelete?.kind === "format"
            ? `The "${pendingPackageDelete.label}" folder and all its files will be permanently removed from storage and the database.`
            : pendingPackageDelete
              ? `"${pendingPackageDelete.filename}" will be permanently removed from storage and the database. If it is the last file in its folder, the folder is removed too.`
              : ""
        }
        cancelLabel="Keep it"
        confirmLabel="Delete permanently"
        confirmVariant="destructive"
        onCancel={() => setPendingPackageDelete(null)}
        onConfirm={() => void confirmPackageDelete()}
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
          </motion.div>
        ) : null}
      </AnimatePresence>
    </>
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
  actionState: PackageActionState | null;
  onDownload: () => void;
  /** True when the organization has Canva configured, so the mirror action should be visible. */
  showSendToCanva: boolean;
  /** True when the Canva action is allowed to run; mirrors the package download ready-state. */
  canSendToCanva: boolean;
  isSendingToCanva: boolean;
  onSendToCanva: () => void;
  /** Request deletion of a whole format folder for a client (opens a confirmation modal). */
  onDeleteFormat: (clientId: string, formatKey: ContentFormatKey, label: string) => void;
  /** Request deletion of a single file/slide within a format (opens a confirmation modal). */
  onDeleteAsset: (
    clientId: string,
    formatKey: ContentFormatKey,
    index: number,
    filename: string
  ) => void;
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
  actionState,
  onDownload,
  showSendToCanva,
  canSendToCanva,
  isSendingToCanva,
  onSendToCanva,
  onDeleteFormat,
  onDeleteAsset,
}: PackageTabContentProps) {
  const { formatTime } = useDateFormatter();
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
      {/* File tree — capped height with its own scroll so the last generated file stays reachable
          when several formats/clients are present instead of being clipped off the bottom. */}
      <div className="max-h-[60vh] overflow-y-auto rounded-lg border border-border/40 bg-muted/20 p-3 space-y-1">
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
                const formatFolder = resolvePackageFormatFolder(formatEntry.formatKey);
                const files = resolvePackageFormatFiles(formatEntry.formatKey, formatEntry.assets);
                return (
                  <div key={formatEntry.formatKey} className="group/format">
                    {/* Format row */}
                    <div className="pl-6 flex items-center gap-1.5 py-0.5">
                      <FolderOpen className="h-3.5 w-3.5 text-amber-400 shrink-0" />
                      <span className="font-mono text-xs text-foreground/80 truncate">
                        {formatFolder}/
                      </span>
                      <span className="ml-auto inline-flex items-center rounded px-1.5 py-0.5 text-[10px] border bg-green-500/10 text-green-600 dark:text-green-400 border-green-500/20 shrink-0">
                        Generated
                      </span>
                      <button
                        type="button"
                        title={`Delete the ${formatFolder} folder`}
                        aria-label={`Delete the ${formatFolder} folder`}
                        onClick={() =>
                          onDeleteFormat(clientEntry.clientId, formatEntry.formatKey, formatFolder)
                        }
                        className="shrink-0 rounded p-0.5 text-muted-foreground/40 opacity-0 transition hover:bg-red-500/10 hover:text-red-500 focus-visible:opacity-100 group-hover/format:opacity-100"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                    {/* Deliverable files are shown as the downloadable package contract, not the
                        stored render-preview asset rows. Story/carousel are editable PPTX; script is MD. */}
                    {files.map((filename, i) => {
                      const assetIndex = formatEntry.assets[i]?.index ?? i;
                      return (
                        <div
                          key={filename}
                          className="group/file pl-12 flex items-center gap-1.5 py-0.5"
                        >
                          <FileText className="h-3.5 w-3.5 text-violet-400 shrink-0" />
                          <span className="font-mono text-xs text-foreground/80 truncate">
                            {filename}
                          </span>
                          <button
                            type="button"
                            title={`Delete ${filename}`}
                            aria-label={`Delete ${filename}`}
                            onClick={() =>
                              onDeleteAsset(
                                clientEntry.clientId,
                                formatEntry.formatKey,
                                assetIndex,
                                filename
                              )
                            }
                            className="ml-auto shrink-0 rounded p-0.5 text-muted-foreground/40 opacity-0 transition hover:bg-red-500/10 hover:text-red-500 focus-visible:opacity-100 group-hover/file:opacity-100"
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                          </button>
                        </div>
                      );
                    })}
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
                        {formatTime(local.generatedAt)}
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

      {actionState && (actionState.downloadCount > 0 || actionState.canvaSendCount > 0) && (
        <div className="grid grid-cols-2 gap-2 text-[11px] text-muted-foreground">
          <div className="rounded-md border border-border/40 bg-muted/20 px-2 py-1.5">
            <span className="font-medium text-foreground/70">Downloaded</span>
            <span className="ml-1">×{actionState.downloadCount}</span>
          </div>
          <div className="rounded-md border border-border/40 bg-muted/20 px-2 py-1.5">
            <span className="font-medium text-foreground/70">Sent to Canva</span>
            <span className="ml-1">×{actionState.canvaSendCount}</span>
          </div>
        </div>
      )}

      {/* Download button */}
      <Button
        size="sm"
        className="w-full h-9 text-xs"
        onClick={onDownload}
        disabled={!hasAnyGeneratedFormat || !canDownloadPackage || isLoading || isSendingToCanva}
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

      {/* Send to Canva — only when the org has Canva configured. Sends the visual package
          (story + carousel slides) as ONE editable multi-page Canva design. */}
      {showSendToCanva && (
        <Button
          size="sm"
          variant="outline"
          className="w-full h-9 text-xs"
          onClick={onSendToCanva}
          disabled={
            !hasAnyGeneratedFormat ||
            !canDownloadPackage ||
            !canSendToCanva ||
            isLoading ||
            isSendingToCanva
          }
          title={
            !hasAnyGeneratedFormat
              ? "Generate at least one format first."
              : !canDownloadPackage
              ? "Package is not ready yet."
              : !canSendToCanva
              ? "A current generation job is required before sending this package to Canva."
              : "Send the visual package (story + carousel) to Canva as one editable design."
          }
        >
          {isSendingToCanva ? (
            <span className="flex items-center gap-2">
              <RefreshCw className="h-3 w-3 animate-spin" />
              Sending to Canva…
            </span>
          ) : (
            <span className="flex items-center gap-2">
              <ExternalLink className="h-3 w-3" />
              Send to Canva
            </span>
          )}
        </Button>
      )}
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
  const { formatDate } = useDateFormatter();
  const gen = item.latestContentGeneration;
  const canDownload = gen?.hasPackage === true && gen.packageJobId !== null;

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

      {gen && generationHistoryLabels(gen.hasPackage, gen.status).map((label, index) => {
        const baseBadge = generationBadge(gen.status);
        const variant: BadgeVariant = index === 0 && gen.hasPackage
          ? "success"
          : label.includes("failed")
            ? "destructive"
            : label.includes("partial")
              ? "warning"
              : baseBadge.variant;
        return (
          <Badge key={label} variant={variant} className="text-xs shrink-0">
            {label}
          </Badge>
        );
      })}

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
