"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import {
  Bookmark,
  Bot,
  CheckCircle2,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Clock3,
  Download,
  ExternalLink,
  FileText,
  Heart,
  History,
  Image,
  Instagram,
  LayoutList,
  Loader2,
  MessageCircle,
  Music2,
  Pencil,
  Play,
  Plus,
  RefreshCcw,
  Share2,
  ShoppingCart,
  Sparkles,
  Trash2,
  Video,
  WandSparkles,
  X,
  Youtube
} from "lucide-react";
import { listOrganizations } from "@/lib/api/organizations";
import { listSocialNetworks } from "@/lib/api/social-networks";
import { getSocialNetworkIcon } from "@/lib/social-network-icons";
import {
  generateContentWorkflowPackage,
  getContentWorkflowHistory,
  getContentWorkflowPreview,
  getContentWorkflowStatus,
  listContentWorkflowHistory,
  listContentWorkflowResults,
  listWorkflowClients,
  runContentWorkflowAction,
  runContentWorkflowBulkAction,
  selectContentWorkflowResults,
  startContentWorkflow
} from "@/lib/api/content-workflow";
import {
  generateMockWorkflowPackage,
  getMockClients,
  getMockOrganizations,
  getMockWorkflowHistory,
  getMockWorkflowPreview,
  getMockWorkflowResults,
  getMockWorkflowStatus,
  listMockWorkflowHistory,
  runMockWorkflowAction,
  selectMockWorkflowResults,
  startMockWorkflow
} from "@/lib/mocks/content-workflow";
import {
  ApiError,
  ContentWorkflowActionType,
  ContentWorkflowHistory,
  ContentWorkflowHistoryItem,
  ContentWorkflowPackage,
  ContentWorkflowPreview,
  ContentWorkflowResult,
  ContentWorkflowSearchCriterion,
  ContentWorkflowStatus,
  ContentWorkflowStep,
  ContentWorkflowStrategyMode,
  Organization,
  PageResult,
  SocialClient,
  SocialNetwork
} from "@/lib/api/types";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { useToast } from "@/components/ui/toaster";
import { cn } from "@/lib/utils";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";

interface PackageCartItem {
  resultId: string;
  title: string;
  networkSource: string;
  strategyMode: string;
  mutationFormat: string;
  thumbnailUrl: string;
  videoUrl: string;
  notes: string;
  addedAtUtc: string;
}

const searchOptions: Array<{ value: ContentWorkflowSearchCriterion; label: string }> = [
  { value: "niche", label: "Niche" },
  { value: "references", label: "References" },
  { value: "viral", label: "Viral" },
  { value: "competitors", label: "Competitors" },
  { value: "own", label: "Own best performers" }
];

const strategyOptions: Array<{ value: ContentWorkflowStrategyMode; label: string }> = [
  { value: "mixed", label: "Mixed" },
  { value: "educational", label: "Educational" },
  { value: "informative", label: "Informative" },
  { value: "entertainment", label: "Entertainment" }
];

const networkOptions = [
  { value: "tiktok", label: "TikTok", icon: Music2 },
  { value: "instagram", label: "Instagram", icon: Instagram },
  { value: "youtube", label: "YouTube", icon: Youtube }
] as const;

const networkMutationFormats: Record<string, Array<{ value: string; label: string }>> = {
  instagram: [
    { value: "reel", label: "Reel" },
    { value: "story_image", label: "Story (image)" },
    { value: "story_video", label: "Story (video)" },
    { value: "carousel_post", label: "Carousel post" },
    { value: "feed_post", label: "Feed post" }
  ],
  tiktok: [
    { value: "video", label: "Video" },
    { value: "slideshow", label: "Slideshow" }
  ],
  youtube: [
    { value: "short", label: "Short" },
    { value: "community_carousel", label: "Community carousel" }
  ]
};

const bulkActionOptions: Array<{ value: ContentWorkflowActionType; label: string; icon: typeof Music2 }> = [
  { value: "transcribe_and_build_script", label: "Transcribe + script", icon: Bot },
  { value: "build_carousel", label: "Build carousel", icon: WandSparkles }
];

const USE_MOCK_WORKFLOW_DATA = true;

type PreviewNetworkTab = "instagram" | "tiktok" | "youtube";
type CandidateViewMode = "grid" | "video";
type RightPanelTab = "conversion" | "preview" | "package";

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

interface WorkflowClientProps {
  isPlatformAdmin: boolean;
}

export function WorkflowClient({ isPlatformAdmin }: WorkflowClientProps) {
  const token = useAccessToken();
  const toast = useToast();
  const { orgId: activeOrgId, isInitialized: orgInitialized } = useActiveOrg();

  // Organization + Client
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState<string>(() =>
    activeOrgId ?? ""
  );
  const [clients, setClients] = useState<SocialClient[]>([]);
  const [selectedClientId, setSelectedClientId] = useState("");

  // Workflow config
  const [searchCriterion, setSearchCriterion] = useState<ContentWorkflowSearchCriterion>("niche");
  const [strategyMode, setStrategyMode] = useState<ContentWorkflowStrategyMode>("mixed");
  const [availableNetworks, setAvailableNetworks] = useState<SocialNetwork[]>([]);
  const [networkSources, setNetworkSources] = useState<string[]>([]);
  const [maxResults, setMaxResults] = useState(12);

  // Workflow state
  const [status, setStatus] = useState<ContentWorkflowStatus | null>(null);
  const [resultsPage, setResultsPage] = useState<PageResult<ContentWorkflowResult> | null>(null);
  const [activeWorkflowId, setActiveWorkflowId] = useState<string | null>(null);
  const [activeResultId, setActiveResultId] = useState<string | null>(null);
  const [selectedResultIds, setSelectedResultIds] = useState<string[]>([]);
  const [historyItems, setHistoryItems] = useState<ContentWorkflowHistoryItem[]>([]);
  const [generatedPackage, setGeneratedPackage] = useState<ContentWorkflowPackage | null>(null);

  // Analysis state (demand-driven — no auto-analyze)
  const [analyzedPreviews, setAnalyzedPreviews] = useState<Record<string, ContentWorkflowPreview>>({});
  const [analyzingResultIds, setAnalyzingResultIds] = useState<Set<string>>(new Set());
  const [analyzedResultIds, setAnalyzedResultIds] = useState<Set<string>>(new Set());

  // Live step animation state
  const [liveSteps, setLiveSteps] = useState<ContentWorkflowStep[]>([]);
  const [bulkAnalyzeProgress, setBulkAnalyzeProgress] = useState<{ done: number; total: number } | null>(null);
  const [zipProgress, setZipProgress] = useState(0);

  // Conversion generation state
  const [generatedScripts, setGeneratedScripts] = useState<Record<string, string>>({});
  const [generatedCarousels, setGeneratedCarousels] = useState<Record<string, string[]>>({});
  const [generatedFlyers, setGeneratedFlyers] = useState<Record<string, string>>({});
  const [generatingConversion, setGeneratingConversion] = useState<Record<string, "script" | "carousel" | "flyer">>({});
  const [cardPipelineRunning, setCardPipelineRunning] = useState<Record<string, boolean>>({});

  // Preview panel tabs + mutation
  const [previewNetworkTab, setPreviewNetworkTab] = useState<PreviewNetworkTab>("instagram");
  const [mutationMode, setMutationMode] = useState<string>(networkMutationFormats.instagram[0]?.value ?? "reel");

  // Package cart
  const [packageCart, setPackageCart] = useState<PackageCartItem[]>([]);
  const [editingCartItemId, setEditingCartItemId] = useState<string | null>(null);
  const [editCartNotes, setEditCartNotes] = useState("");
  const [editCartMutation, setEditCartMutation] = useState("");

  // UI state
  const [candidateViewMode, setCandidateViewMode] = useState<CandidateViewMode>("video");
  const [rightPanelTab, setRightPanelTab] = useState<RightPanelTab>("conversion");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(12);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // History expand + detail map
  const [expandedHistoryId, setExpandedHistoryId] = useState<string | null>(null);
  const [historyDetailMap, setHistoryDetailMap] = useState<Record<string, ContentWorkflowHistory>>({});

  // Re-download tracking
  const [downloadedWorkflowIds, setDownloadedWorkflowIds] = useState<Set<string>>(new Set());

  // Refs
  const animationGenerationRef = useRef(0);
  const analyzedPreviewsRef = useRef<Record<string, ContentWorkflowPreview>>({});
  const cardPipelineRef = useRef<Set<string>>(new Set());

  const selectedClient = useMemo(
    () => clients.find((item) => item.id === selectedClientId) ?? null,
    [clients, selectedClientId]
  );

  const results = resultsPage?.items ?? [];

  const activeResult = useMemo(
    () => results.find((item) => item.id === activeResultId) ?? null,
    [results, activeResultId]
  );

  const preview = useMemo(
    () => (activeResultId ? (analyzedPreviews[activeResultId] ?? null) : null),
    [activeResultId, analyzedPreviews]
  );

  useEffect(() => {
    if (!orgInitialized) return;
    if (!USE_MOCK_WORKFLOW_DATA && !token) {
      setLoading(false);
      return;
    }

    void bootstrap(token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, orgInitialized]);

  useEffect(() => {
    if (!isPlatformAdmin && activeOrgId) {
      setSelectedOrganizationId(activeOrgId);
    }
  }, [isPlatformAdmin, activeOrgId]);

  useEffect(() => {
    if (!selectedOrganizationId || (!USE_MOCK_WORKFLOW_DATA && !token)) {
      return;
    }

    void loadOrganizationContext(selectedOrganizationId, token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedOrganizationId, token]);

  useEffect(() => {
    if (!selectedOrganizationId || !activeWorkflowId || (!USE_MOCK_WORKFLOW_DATA && !token)) {
      return;
    }

    void loadWorkflowStatus(activeWorkflowId, selectedOrganizationId, token);
    const interval = window.setInterval(() => {
      void loadWorkflowStatus(activeWorkflowId, selectedOrganizationId, token);
    }, 2500);

    return () => window.clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeWorkflowId, selectedOrganizationId, token]);

  useEffect(() => {
    if (!selectedOrganizationId || !activeWorkflowId || (!USE_MOCK_WORKFLOW_DATA && !token)) {
      return;
    }

    void loadWorkflowResults(activeWorkflowId, selectedOrganizationId, pageNumber, token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeWorkflowId, selectedOrganizationId, pageNumber, token]);

  useEffect(() => {
    if (!status?.steps?.length) return;
    const gen = ++animationGenerationRef.current;
    void driveStepAnimation(status.steps, gen);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status?.steps]);

  async function bootstrap(accessToken?: string) {
    setLoading(true);
    setError(null);

    try {
      // Always load real networks from the catalog
      const networksResult = await listSocialNetworks({ includeInactive: false, pageSize: 100 }, accessToken).catch(() => null);
      if (networksResult) {
        setAvailableNetworks(networksResult.items);
        setNetworkSources(networksResult.items.slice(0, 2).map((n) => n.code));
      }

      if (USE_MOCK_WORKFLOW_DATA) {
        const nextOrganizations = getMockOrganizations();
        setOrganizations(nextOrganizations);
        const nextOrganizationId = nextOrganizations[0]?.id ?? "";
        setSelectedOrganizationId(nextOrganizationId);
      } else if (isPlatformAdmin) {
        const nextOrganizations = (await listOrganizations({ page: 1, pageSize: 100, includeInactive: false }, accessToken!)).items;
        setOrganizations(nextOrganizations);
        const nextOrganizationId = nextOrganizations[0]?.id ?? "";
        setSelectedOrganizationId(nextOrganizationId);
      } else {
        setSelectedOrganizationId(activeOrgId ?? "");
      }
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }

  async function loadOrganizationContext(organizationId: string, accessToken?: string) {
    setBusy(true);
    setError(null);

    try {
      // Always load real clients regardless of mock flag
      const nextClients = accessToken
        ? await listWorkflowClients(organizationId, accessToken).then((response) => response.items)
        : getMockClients(organizationId);

      const nextHistory: ContentWorkflowHistoryItem[] = USE_MOCK_WORKFLOW_DATA
        ? listMockWorkflowHistory(organizationId, 12)
        : await listContentWorkflowHistory(organizationId, 12, accessToken!);

      setClients(nextClients);
      setSelectedClientId((current) => current || nextClients[0]?.id || "");
      setHistoryItems(nextHistory);
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setBusy(false);
    }
  }

  async function startGeneration() {
    if (!selectedOrganizationId || !selectedClient || (!USE_MOCK_WORKFLOW_DATA && !token)) {
      return;
    }

    setBusy(true);
    setError(null);
    setGeneratedPackage(null);
    setPackageCart([]);
    setAnalyzedPreviews({});
    analyzedPreviewsRef.current = {};
    setAnalyzedResultIds(new Set());
    setAnalyzingResultIds(new Set());

    try {
      const payload = {
        clientId: selectedClient.id,
        clientName: selectedClient.name,
        searchCriterion,
        strategyMode,
        networkSources,
        maxResults
      };
      const session = USE_MOCK_WORKFLOW_DATA
        ? startMockWorkflow(selectedOrganizationId, payload)
        : await startContentWorkflow(selectedOrganizationId, payload, token!);

      setActiveWorkflowId(session.workflowId);
      setSelectedResultIds([]);
      setPageNumber(1);
      setActiveResultId(null);
      toast.success("Workflow started", `Running generation for ${selectedClient.name}.`);
      await Promise.all([
        loadWorkflowStatus(session.workflowId, selectedOrganizationId, token),
        loadWorkflowResults(session.workflowId, selectedOrganizationId, 1, token),
        refreshHistory(selectedOrganizationId, token)
      ]);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not start workflow", message);
    } finally {
      setBusy(false);
    }
  }

  async function loadWorkflowStatus(workflowId: string, organizationId: string, accessToken?: string) {
    try {
      const nextStatus = USE_MOCK_WORKFLOW_DATA
        ? getMockWorkflowStatus(organizationId, workflowId)
        : await getContentWorkflowStatus(organizationId, workflowId, accessToken!);
      setStatus(nextStatus);
    } catch (err) {
      setError((err as ApiError).message);
    }
  }

  async function loadWorkflowResults(workflowId: string, organizationId: string, page: number, accessToken?: string) {
    try {
      const pageResult = USE_MOCK_WORKFLOW_DATA
        ? getMockWorkflowResults(organizationId, workflowId, page, pageSize)
        : await listContentWorkflowResults(organizationId, workflowId, page, pageSize, accessToken!);
      setResultsPage(pageResult);
      if (!activeResultId && pageResult.items.length > 0) {
        setActiveResultId(pageResult.items[0].id);
      }
    } catch (err) {
      setError((err as ApiError).message);
    }
  }

  async function refreshHistory(organizationId: string, accessToken?: string) {
    const history = USE_MOCK_WORKFLOW_DATA
      ? listMockWorkflowHistory(organizationId, 12)
      : await listContentWorkflowHistory(organizationId, 12, accessToken!);
    setHistoryItems(history);
  }

  function toggleNetwork(network: string) {
    setNetworkSources((current) => {
      if (current.includes(network)) {
        const next = current.filter((item) => item !== network);
        return next.length > 0 ? next : current;
      }

      return [...current, network];
    });
  }

  function toggleSelection(resultId: string) {
    setSelectedResultIds((current) =>
      current.includes(resultId) ? current.filter((item) => item !== resultId) : [...current, resultId]
    );
  }

  async function saveSelection() {
    if (!selectedOrganizationId || !activeWorkflowId || selectedResultIds.length === 0 || (!USE_MOCK_WORKFLOW_DATA && !token)) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const selection = USE_MOCK_WORKFLOW_DATA
        ? selectMockWorkflowResults(selectedOrganizationId, activeWorkflowId, selectedResultIds)
        : await selectContentWorkflowResults(selectedOrganizationId, activeWorkflowId, selectedResultIds, token!);
      setSelectedResultIds(selection.selectedResultIds);
      toast.success("Selection saved", `${selection.selectedCount} content items selected.`);
      await refreshHistory(selectedOrganizationId, token);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not save selection", message);
    } finally {
      setBusy(false);
    }
  }

  async function runSingleAction(action: ContentWorkflowActionType, resultId: string) {
    if (!selectedOrganizationId || !activeWorkflowId || (!USE_MOCK_WORKFLOW_DATA && !token)) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = USE_MOCK_WORKFLOW_DATA
        ? runMockWorkflowAction(selectedOrganizationId, activeWorkflowId, action, [resultId])
        : await runContentWorkflowAction(selectedOrganizationId, activeWorkflowId, resultId, action, token!);
      toast.success("Action completed", response.message);
      await refreshHistory(selectedOrganizationId, token);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Action failed", message);
    } finally {
      setBusy(false);
    }
  }

  async function runBulkAction(action: ContentWorkflowActionType) {
    if (!selectedOrganizationId || !activeWorkflowId || selectedResultIds.length === 0 || (!USE_MOCK_WORKFLOW_DATA && !token)) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = USE_MOCK_WORKFLOW_DATA
        ? runMockWorkflowAction(selectedOrganizationId, activeWorkflowId, action, selectedResultIds)
        : await runContentWorkflowBulkAction(
            selectedOrganizationId,
            activeWorkflowId,
            action,
            selectedResultIds,
            token!
          );
      toast.success("Bulk action completed", response.message);
      await refreshHistory(selectedOrganizationId, token);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Bulk action failed", message);
    } finally {
      setBusy(false);
    }
  }

  async function analyzeResult(resultId: string): Promise<ContentWorkflowPreview | null> {
    if (
      !selectedOrganizationId ||
      !activeWorkflowId ||
      analyzedResultIds.has(resultId) ||
      analyzingResultIds.has(resultId) ||
      (!USE_MOCK_WORKFLOW_DATA && !token)
    ) {
      return analyzedPreviewsRef.current[resultId] ?? null;
    }

    setAnalyzingResultIds((prev) => new Set([...prev, resultId]));
    setActiveResultId(resultId);
    setRightPanelTab("conversion");

    try {
      const nextPreview = USE_MOCK_WORKFLOW_DATA
        ? getMockWorkflowPreview(selectedOrganizationId, activeWorkflowId, resultId)
        : await getContentWorkflowPreview(selectedOrganizationId, activeWorkflowId, resultId, token!);
      analyzedPreviewsRef.current = { ...analyzedPreviewsRef.current, [resultId]: nextPreview };
      setAnalyzedPreviews((prev) => ({ ...prev, [resultId]: nextPreview }));
      setAnalyzedResultIds((prev) => new Set([...prev, resultId]));
      return nextPreview;
    } catch (err) {
      toast.error("Analysis failed", (err as ApiError).message);
      return null;
    } finally {
      setAnalyzingResultIds((prev) => {
        const next = new Set(prev);
        next.delete(resultId);
        return next;
      });
    }
  }

  async function analyzeBulk() {
    const ids = selectedResultIds.filter((id) => !analyzedResultIds.has(id) && !analyzingResultIds.has(id));
    setBulkAnalyzeProgress({ done: 0, total: ids.length });
    for (let i = 0; i < ids.length; i++) {
      await analyzeResult(ids[i]!);
      setBulkAnalyzeProgress({ done: i + 1, total: ids.length });
    }
    setBulkAnalyzeProgress(null);
  }

  function addToPackage() {
    if (!activeResult || !analyzedResultIds.has(activeResult.id)) return;

    if (packageCart.some((item) => item.resultId === activeResult.id && item.mutationFormat === mutationMode)) {
      toast.error("Already in package", `"${activeResult.title}" (${mutationMode}) is already in the package.`);
      return;
    }

    const newItem: PackageCartItem = {
      resultId: activeResult.id,
      title: activeResult.title,
      networkSource: activeResult.networkSource,
      strategyMode: activeResult.strategyMode,
      mutationFormat: mutationMode,
      thumbnailUrl: activeResult.thumbnailUrl,
      videoUrl: activeResult.videoUrl,
      notes: "",
      addedAtUtc: new Date().toISOString()
    };

    setPackageCart((prev) => [...prev, newItem]);
    toast.success("Added to package", activeResult.title);
  }

  function removeFromCart(resultId: string) {
    setPackageCart((prev) => prev.filter((item) => item.resultId !== resultId));
  }

  function startCartItemEdit(item: PackageCartItem) {
    setEditingCartItemId(item.resultId);
    setEditCartNotes(item.notes);
    setEditCartMutation(item.mutationFormat);
  }

  function saveCartItemEdit(resultId: string) {
    setPackageCart((prev) =>
      prev.map((item) =>
        item.resultId === resultId ? { ...item, mutationFormat: editCartMutation, notes: editCartNotes } : item
      )
    );
    setEditingCartItemId(null);
  }

  async function generatePackage() {
    if (!selectedOrganizationId || !activeWorkflowId || packageCart.length === 0 || (!USE_MOCK_WORKFLOW_DATA && !token)) {
      return;
    }

    setBusy(true);
    setError(null);
    setZipProgress(0);

    const progressInterval = window.setInterval(() => {
      setZipProgress((prev) => Math.min(prev + 8, 92));
    }, 200);

    try {
      const cartResultIds = packageCart.map((item) => item.resultId);

      if (USE_MOCK_WORKFLOW_DATA) {
        selectMockWorkflowResults(selectedOrganizationId, activeWorkflowId, cartResultIds);
      } else {
        await selectContentWorkflowResults(selectedOrganizationId, activeWorkflowId, cartResultIds, token!);
      }

      const packageResponse = USE_MOCK_WORKFLOW_DATA
        ? generateMockWorkflowPackage(selectedOrganizationId, activeWorkflowId)
        : await generateContentWorkflowPackage(selectedOrganizationId, activeWorkflowId, token!);

      setZipProgress(100);
      setGeneratedPackage(packageResponse);
      toast.success("Package ready", packageResponse.packageFileName);
      await refreshHistory(selectedOrganizationId, token);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not generate package", message);
    } finally {
      window.clearInterval(progressInterval);
      setBusy(false);
    }
  }

  async function resumeWorkflow(history: ContentWorkflowHistoryItem) {
    if (!selectedOrganizationId || (!USE_MOCK_WORKFLOW_DATA && !token)) return;
    if (activeWorkflowId === history.workflowId) return;

    setBusy(true);
    setError(null);

    try {
      setActiveWorkflowId(history.workflowId);
      setPageNumber(1);
      setActiveResultId(null);
      setSelectedResultIds([]);
      setPackageCart([]);
      setAnalyzedPreviews({});
      analyzedPreviewsRef.current = {};
      setAnalyzedResultIds(new Set());
      setAnalyzingResultIds(new Set());
      await Promise.all([
        loadWorkflowStatus(history.workflowId, selectedOrganizationId, token),
        loadWorkflowResults(history.workflowId, selectedOrganizationId, 1, token)
      ]);
      toast.success("Workflow resumed", history.clientName);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not resume workflow", message);
    } finally {
      setBusy(false);
    }
  }

  async function toggleHistoryExpand(item: ContentWorkflowHistoryItem) {
    if (expandedHistoryId === item.workflowId) {
      setExpandedHistoryId(null);
      return;
    }

    setExpandedHistoryId(item.workflowId);

    if (!historyDetailMap[item.workflowId] && selectedOrganizationId) {
      try {
        const detail = USE_MOCK_WORKFLOW_DATA
          ? getMockWorkflowHistory(selectedOrganizationId, item.workflowId)
          : await getContentWorkflowHistory(selectedOrganizationId, item.workflowId, token!);
        setHistoryDetailMap((prev) => ({ ...prev, [item.workflowId]: detail }));
      } catch (err) {
        toast.error("Could not load history", (err as ApiError).message);
      }
    }
  }

  async function redownloadPackage(workflowId: string) {
    if (!selectedOrganizationId || (!USE_MOCK_WORKFLOW_DATA && !token)) return;

    try {
      const packageResponse = USE_MOCK_WORKFLOW_DATA
        ? generateMockWorkflowPackage(selectedOrganizationId, workflowId)
        : await generateContentWorkflowPackage(selectedOrganizationId, workflowId, token!);
      setDownloadedWorkflowIds((prev) => new Set([...prev, workflowId]));
      toast.success("Package re-downloaded", packageResponse.packageFileName);
    } catch (err) {
      toast.error("Re-download failed", (err as ApiError).message);
    }
  }

  function handleNetworkTabChange(network: PreviewNetworkTab) {
    setPreviewNetworkTab(network);
    const formats = networkMutationFormats[network];
    if (formats && formats.length > 0) {
      setMutationMode(formats[0].value);
    }
  }

  async function driveStepAnimation(steps: ContentWorkflowStep[], generation: number) {
    for (const step of steps) {
      if (animationGenerationRef.current !== generation) return;
      setLiveSteps((prev) => {
        const exists = prev.some((s) => s.key === step.key);
        if (exists) return prev.map((s) => (s.key === step.key ? step : s));
        return [...prev, step];
      });
      await sleep(120);
    }
  }

  async function generateScript(resultId: string) {
    setGeneratingConversion((prev) => ({ ...prev, [resultId]: "script" }));
    await sleep(1200);
    setGeneratedScripts((prev) => ({
      ...prev,
      [resultId]: `Hook: Did you know this changes everything?\n\nBody: [Key insight from the content]\n\nCTA: Follow for more content like this!`
    }));
    setGeneratingConversion((prev) => {
      const next = { ...prev };
      delete next[resultId];
      return next;
    });
  }

  async function generateCarousel(resultId: string) {
    setGeneratingConversion((prev) => ({ ...prev, [resultId]: "carousel" }));
    await sleep(1400);
    setGeneratedCarousels((prev) => ({
      ...prev,
      [resultId]: ["Slide 1: Hook — grab attention", "Slide 2: Problem statement", "Slide 3: Key insight", "Slide 4: Proof / example", "Slide 5: CTA — follow for more"]
    }));
    setGeneratingConversion((prev) => {
      const next = { ...prev };
      delete next[resultId];
      return next;
    });
  }

  async function generateFlyer(resultId: string) {
    setGeneratingConversion((prev) => ({ ...prev, [resultId]: "flyer" }));
    await sleep(1000);
    setGeneratedFlyers((prev) => ({
      ...prev,
      [resultId]: `Headline: [Bold statement]\nSubtext: [Supporting detail]\nVisual: High-contrast image with brand colors\nCTA: [Action verb] + link in bio`
    }));
    setGeneratingConversion((prev) => {
      const next = { ...prev };
      delete next[resultId];
      return next;
    });
  }

  async function runCardPipeline(resultId: string, type: "script" | "carousel" | "flyer") {
    if (cardPipelineRef.current.has(resultId)) return;
    cardPipelineRef.current.add(resultId);
    setCardPipelineRunning((prev) => ({ ...prev, [resultId]: true }));

    try {
      if (!analyzedResultIds.has(resultId)) {
        await analyzeResult(resultId);
      }
      if (type === "script") await generateScript(resultId);
      else if (type === "carousel") await generateCarousel(resultId);
      else await generateFlyer(resultId);
    } finally {
      cardPipelineRef.current.delete(resultId);
      setCardPipelineRunning((prev) => {
        const next = { ...prev };
        delete next[resultId];
        return next;
      });
    }
  }

  if (loading) {
    return (
      <div className="flex min-h-[16rem] items-center justify-center">
        <Loader2 className="h-6 w-6 animate-spin text-primary" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* ── Header ── */}
      <section className="rounded-3xl border border-border bg-card p-6">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div className="space-y-3">
            <Badge variant="success" className="w-fit">
              <Sparkles className="mr-1 h-3 w-3" /> Generator
            </Badge>
            <div>
              <h1 className="text-3xl font-semibold tracking-tight md:text-5xl">Generator</h1>
              <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
                Select your client, search criteria, and strategy. Generate candidate videos, analyze them with AI, pick your winners, and export a complete monthly content package.
              </p>
            </div>
          </div>
          <div className="flex flex-wrap items-end gap-3">
            {isPlatformAdmin && organizations.length > 0 && (
              <div className="flex flex-col gap-1">
                <span className="text-xs font-medium uppercase tracking-widest text-muted-foreground">Organization</span>
                <Select
                  className="min-w-[220px]"
                  value={selectedOrganizationId}
                  onChange={(event) => setSelectedOrganizationId(event.target.value)}
                >
                  {organizations.map((organization) => (
                    <option key={organization.id} value={organization.id}>
                      {organization.name}
                    </option>
                  ))}
                </Select>
              </div>
            )}
            <Button
              variant="outline"
              onClick={() => {
                if (!selectedOrganizationId || (!USE_MOCK_WORKFLOW_DATA && !token)) return;
                void loadOrganizationContext(selectedOrganizationId, token);
              }}
            >
              <RefreshCcw className={cn("h-4 w-4", busy && "animate-spin")} /> Refresh
            </Button>
          </div>
        </div>
      </section>

      {/* ── Error ── */}
      {error ? (
        <div className="rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-100">{error}</div>
      ) : null}

      {/* ── Configuration ── */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Configuration</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4 lg:grid-cols-2 xl:grid-cols-6">
          <div className="space-y-2">
            <label className="text-sm font-medium">Client</label>
            <Select
              className="w-full"
              value={selectedClientId}
              onChange={(event) => setSelectedClientId(event.target.value)}
            >
              <option value="" disabled>
                Select client
              </option>
              {clients.map((client) => (
                <option key={client.id} value={client.id}>
                  {client.name}
                </option>
              ))}
            </Select>
          </div>
          <div className="space-y-2">
            <label className="text-sm font-medium">Search criterion</label>
            <Select
              className="w-full"
              value={searchCriterion}
              onChange={(event) => setSearchCriterion(event.target.value as ContentWorkflowSearchCriterion)}
            >
              {searchOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="space-y-2">
            <label className="text-sm font-medium">Content strategy</label>
            <Select
              className="w-full"
              value={strategyMode}
              onChange={(event) => setStrategyMode(event.target.value as ContentWorkflowStrategyMode)}
            >
              {strategyOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="space-y-2">
            <label className="text-sm font-medium">Max results</label>
            <input
              type="number"
              min={1}
              max={100}
              className="h-10 w-full rounded-md border border-border bg-background px-3 text-sm"
              value={maxResults}
              onChange={(event) => setMaxResults(Number(event.target.value))}
            />
            <p className="text-xs text-muted-foreground">Limit candidates to save AI analysis tokens.</p>
          </div>
          <div className="space-y-2 xl:col-span-2">
            <label className="text-sm font-medium">Source networks</label>
            {availableNetworks.length === 0 ? (
              <p className="text-xs text-muted-foreground">Loading networks…</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {/* Selected — highlighted chips with remove (×) */}
                {networkSources.map((code) => {
                  const network = availableNetworks.find((n) => n.code === code);
                  if (!network) return null;
                  const Icon = getSocialNetworkIcon(code);
                  return (
                    <span
                      key={code}
                      className="flex items-center gap-1.5 rounded-full border border-primary/50 bg-primary/15 px-3 py-1 text-xs font-medium text-primary"
                    >
                      <Icon className="h-3.5 w-3.5 shrink-0" />
                      {network.displayName}
                      <button
                        type="button"
                        onClick={() => toggleNetwork(code)}
                        className="ml-0.5 text-primary/60 hover:text-primary transition-colors"
                        aria-label={`Remove ${network.displayName}`}
                      >
                        <X className="h-3 w-3" />
                      </button>
                    </span>
                  );
                })}
                {/* Available but not selected — muted chips with add (+) */}
                {availableNetworks
                  .filter((n) => !networkSources.includes(n.code))
                  .map((network) => {
                    const Icon = getSocialNetworkIcon(network.code);
                    return (
                      <button
                        key={network.code}
                        type="button"
                        onClick={() => toggleNetwork(network.code)}
                        className="flex items-center gap-1.5 rounded-full border border-border/60 bg-background/60 px-3 py-1 text-xs text-muted-foreground hover:border-primary/40 hover:text-foreground transition-colors"
                        aria-label={`Add ${network.displayName}`}
                      >
                        <Icon className="h-3.5 w-3.5 shrink-0" />
                        {network.displayName}
                        <Plus className="h-3 w-3" />
                      </button>
                    );
                  })}
              </div>
            )}
          </div>
          <div className="lg:col-span-2 xl:col-span-6">
            <Button
              onClick={() => void startGeneration()}
              disabled={busy || !selectedClientId || !selectedOrganizationId || networkSources.length === 0}
            >
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
              Generate
            </Button>
          </div>
        </CardContent>
      </Card>

      {/* ── Generation progress ── */}
      {(
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              <span>Generation progress</span>
              <Badge variant={status?.status === "completed" ? "success" : "muted"}>{status?.status ?? "idle"}</Badge>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="h-2 overflow-hidden rounded-full bg-muted">
              <div className="h-full bg-primary transition-all" style={{ width: `${status?.progressPercentage ?? 0}%` }} />
            </div>
            <p className="text-sm text-muted-foreground">{status?.progressMessage ?? "Ready to generate"}</p>
            <div className="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
              {(liveSteps.length > 0 ? liveSteps : (status?.steps ?? [])).map((step) => (
                <div key={step.key} className="rounded-xl border border-border bg-background/40 p-3">
                  <p className="text-sm font-medium">{step.label}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{step.message}</p>
                  <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-muted">
                    <div
                      className="h-full rounded-full bg-primary transition-all"
                      style={{ width: `${step.progressPercentage}%` }}
                    />
                  </div>
                  <Badge
                    variant={
                      step.status === "completed" ? "success" : step.status === "in_progress" ? "default" : "muted"
                    }
                    className="mt-2 w-fit"
                  >
                    {step.status}
                  </Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* ── Main grid ── */}
      <div className="grid gap-4 2xl:grid-cols-[1.45fr_1.05fr]">
        {/* ── Candidate Results ── */}
        <Card className="overflow-hidden">
          <CardHeader className="border-b border-border">
            <CardTitle className="flex items-center justify-between text-base">
              <span>Candidate results</span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant={candidateViewMode === "grid" ? "default" : "outline"}
                  className="h-7 px-2"
                  title="Grid view"
                  onClick={() => setCandidateViewMode("grid")}
                >
                  <LayoutList className="h-4 w-4" />
                </Button>
                <Button
                  size="sm"
                  variant={candidateViewMode === "video" ? "default" : "outline"}
                  className="h-7 px-2"
                  title="Video view"
                  onClick={() => setCandidateViewMode("video")}
                >
                  <Play className="h-4 w-4" />
                </Button>
                <span className="text-xs font-normal text-muted-foreground">
                  Page {resultsPage?.page ?? pageNumber} of {Math.max(resultsPage?.totalPages ?? 1, 1)}
                </span>
              </div>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4 p-4">
            {/* Toolbar */}
            <div className="flex flex-wrap gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setSelectedResultIds(results.map((r) => r.id))}
              >
                Select all
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={selectedResultIds.length === 0}
                onClick={() => setSelectedResultIds([])}
              >
                Deselect all
              </Button>
              <Button
                variant="secondary"
                size="sm"
                disabled={selectedResultIds.length === 0 || busy}
                onClick={() => void saveSelection()}
              >
                <CheckCircle2 className="h-4 w-4" /> Save selection ({selectedResultIds.length})
              </Button>
              <Button
                variant="secondary"
                size="sm"
                disabled={selectedResultIds.length === 0 || analyzingResultIds.size > 0}
                onClick={() => void analyzeBulk()}
              >
                <Bot className="h-4 w-4" /> Analyze selected
              </Button>
              {bulkActionOptions.map((action) => {
                const Icon = action.icon;
                return (
                  <Button
                    key={action.value}
                    variant="outline"
                    size="sm"
                    disabled={selectedResultIds.length === 0 || busy}
                    onClick={() => void runBulkAction(action.value)}
                  >
                    <Icon className="h-4 w-4" /> {action.label}
                  </Button>
                );
              })}
            </div>

            {bulkAnalyzeProgress ? (
              <div className="space-y-1">
                <p className="text-xs text-muted-foreground">
                  Analyzing {bulkAnalyzeProgress.done} / {bulkAnalyzeProgress.total}…
                </p>
                <div className="h-1.5 overflow-hidden rounded-full bg-muted">
                  <div
                    className="h-full bg-primary transition-all"
                    style={{ width: `${(bulkAnalyzeProgress.done / bulkAnalyzeProgress.total) * 100}%` }}
                  />
                </div>
              </div>
            ) : null}

            {/* Results */}
            {results.length === 0 ? (
              <div className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground">
                Generate the workflow to load paginated content candidates.
              </div>
            ) : (
              <div className={cn("gap-3", candidateViewMode === "grid" ? "grid md:grid-cols-2" : "grid grid-cols-1")}>
                {results.map((result) => {
                  const inPackage = packageCart.some((item) => item.resultId === result.id);
                  return (
                    <article
                      key={result.id}
                      className={cn(
                        "rounded-2xl border bg-background/40 p-4",
                        activeResultId === result.id
                          ? "border-primary/70 bg-primary/10"
                          : inPackage
                            ? "border-emerald-500/50"
                            : "border-border"
                      )}
                    >
                      <div className={cn(candidateViewMode === "video" && "flex items-start gap-3")}>
                        {candidateViewMode === "video" && (
                          <a href={result.videoUrl} target="_blank" rel="noreferrer" className="flex-shrink-0">
                            <img
                              src={result.thumbnailUrl}
                              alt={result.title}
                              className="h-12 w-[72px] rounded-md object-cover"
                            />
                          </a>
                        )}
                        <div className={cn(candidateViewMode === "video" && "min-w-0 flex-1")}>
                        {/* Header row */}
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <label className="flex items-center gap-2 text-xs text-muted-foreground">
                            <input
                              type="checkbox"
                              checked={selectedResultIds.includes(result.id)}
                              onChange={() => toggleSelection(result.id)}
                            />
                            Select
                          </label>
                          <div className="flex flex-wrap items-center gap-1.5">
                            {inPackage && (
                              <Badge variant="success" className="text-[10px]">
                                In package
                              </Badge>
                            )}
                            <Badge
                              variant={strategyBadgeVariant(result.strategyMode)}
                              className="text-[10px] uppercase tracking-widest"
                            >
                              {result.strategyMode}
                            </Badge>
                            {(() => {
                              const NetIcon = getSocialNetworkIcon(result.networkSource);
                              const networkLabel = availableNetworks.find((n) => n.code === result.networkSource)?.displayName ?? result.networkSource;
                              return (
                                <span title={networkLabel}>
                                  <NetIcon className="h-4 w-4 text-muted-foreground shrink-0" />
                                </span>
                              );
                            })()}
                          </div>
                        </div>

                        {/* Body: click → select + switch to conversion */}
                        <button
                          type="button"
                          className="mt-2 w-full text-left"
                          onClick={() => {
                            setActiveResultId(result.id);
                            setRightPanelTab("conversion");
                          }}
                        >
                          <p className="font-semibold">{result.title}</p>
                          <p className="mt-1 text-xs text-muted-foreground">{result.summary}</p>
                        </button>

                        {/* Stats */}
                        <div className="mt-3 grid grid-cols-3 gap-2 text-xs text-muted-foreground">
                          <Fact label="Views" value={result.views.toLocaleString()} />
                          <Fact label="Likes" value={result.likes.toLocaleString()} />
                          <Fact label="Comments" value={result.comments.toLocaleString()} />
                        </div>

                        {/* Per-card actions */}
                        <div className="mt-3 flex flex-wrap gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={cardPipelineRunning[result.id]}
                            onClick={() => void runCardPipeline(result.id, "script")}
                          >
                            {generatingConversion[result.id] === "script" ? (
                              <Loader2 className="h-4 w-4 animate-spin" />
                            ) : (
                              <FileText className="h-4 w-4" />
                            )}
                            Script
                          </Button>
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={cardPipelineRunning[result.id]}
                            onClick={() => void runCardPipeline(result.id, "carousel")}
                          >
                            {generatingConversion[result.id] === "carousel" ? (
                              <Loader2 className="h-4 w-4 animate-spin" />
                            ) : (
                              <Image className="h-4 w-4" />
                            )}
                            Carousel
                          </Button>
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={cardPipelineRunning[result.id]}
                            onClick={() => void runCardPipeline(result.id, "flyer")}
                          >
                            {generatingConversion[result.id] === "flyer" ? (
                              <Loader2 className="h-4 w-4 animate-spin" />
                            ) : (
                              <Bookmark className="h-4 w-4" />
                            )}
                            Flyer
                          </Button>
                          {/* Analyze button */}
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={analyzedResultIds.has(result.id)}
                            onClick={(e) => {
                              e.stopPropagation();
                              void analyzeResult(result.id);
                            }}
                          >
                            {analyzingResultIds.has(result.id) ? (
                              <Loader2 className="h-4 w-4 animate-spin" />
                            ) : analyzedResultIds.has(result.id) ? (
                              <CheckCircle2 className="h-4 w-4 text-emerald-400" />
                            ) : (
                              <Bot className="h-4 w-4" />
                            )}
                            {analyzedResultIds.has(result.id) ? "Analyzed" : "Analyze"}
                          </Button>
                        </div>
                      </div>
                    </div>
                    </article>
                  );
                })}
              </div>
            )}

            {/* Pagination */}
            <div className="flex items-center justify-between border-t border-border pt-4">
              <Button
                variant="outline"
                disabled={!resultsPage?.hasPreviousPage || busy}
                onClick={() => setPageNumber((current) => Math.max(1, current - 1))}
              >
                <ChevronLeft className="h-4 w-4" /> Previous
              </Button>
              <span className="text-xs text-muted-foreground">
                {resultsPage?.totalCount ?? 0} results • {pageSize} per page
              </span>
              <Button
                variant="outline"
                disabled={!resultsPage?.hasNextPage || busy}
                onClick={() => setPageNumber((current) => current + 1)}
              >
                Next <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          </CardContent>
        </Card>

        {/* ── Right Panel ── */}
        <Card>
          <CardHeader className="border-b border-border">
            <CardTitle className="flex items-center gap-2 text-base">
              <Button
                size="sm"
                variant={rightPanelTab === "conversion" ? "default" : "outline"}
                onClick={() => setRightPanelTab("conversion")}
              >
                <WandSparkles className="h-4 w-4" /> Analysis &amp; Conversion
              </Button>
              <Button
                size="sm"
                variant={rightPanelTab === "preview" ? "default" : "outline"}
                onClick={() => setRightPanelTab("preview")}
              >
                Preview
              </Button>
              <Button
                size="sm"
                variant={rightPanelTab === "package" ? "default" : "outline"}
                onClick={() => setRightPanelTab("package")}
              >
                <ShoppingCart className="h-4 w-4" />
                Package
                {packageCart.length > 0 && (
                  <Badge variant="success" className="ml-1 text-[10px]">
                    {packageCart.length}
                  </Badge>
                )}
              </Button>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 p-4">
            {rightPanelTab === "conversion" ? (
              /* ── Analysis & Conversion tab ── */
              <>
                {preview ? (
                  <>
                    <div className="rounded-xl border border-border bg-background/40 p-3">
                      <p className="font-medium">{preview.resultTitle}</p>
                      <p className="mt-1 text-xs text-muted-foreground">{preview.videoAnalysis}</p>
                    </div>

                    <div className="space-y-2">
                      <p className="text-xs font-medium uppercase tracking-widest text-muted-foreground">
                        Script draft
                      </p>
                      {generatedScripts[activeResultId ?? ""] ? (
                        <div className="rounded-xl border border-border bg-background/40 p-3 text-sm whitespace-pre-line">
                          {generatedScripts[activeResultId ?? ""]}
                        </div>
                      ) : (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={!!generatingConversion[activeResultId ?? ""]}
                          onClick={() => activeResultId && void generateScript(activeResultId)}
                        >
                          {generatingConversion[activeResultId ?? ""] === "script" ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <FileText className="h-4 w-4" />
                          )}
                          Generate script
                        </Button>
                      )}
                    </div>

                    <div className="space-y-2">
                      <p className="text-xs font-medium uppercase tracking-widest text-muted-foreground">
                        Carousel slides
                      </p>
                      {generatedCarousels[activeResultId ?? ""] ? (
                        <ol className="space-y-1">
                          {generatedCarousels[activeResultId ?? ""]!.map((slide, i) => (
                            <li key={i} className="rounded-lg border border-border bg-background/40 px-3 py-2 text-sm">
                              {slide}
                            </li>
                          ))}
                        </ol>
                      ) : (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={!!generatingConversion[activeResultId ?? ""]}
                          onClick={() => activeResultId && void generateCarousel(activeResultId)}
                        >
                          {generatingConversion[activeResultId ?? ""] === "carousel" ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Image className="h-4 w-4" />
                          )}
                          Generate carousel
                        </Button>
                      )}
                    </div>

                    <div className="space-y-2">
                      <p className="text-xs font-medium uppercase tracking-widest text-muted-foreground">
                        Flyer concept
                      </p>
                      {generatedFlyers[activeResultId ?? ""] ? (
                        <div className="rounded-xl border border-border bg-background/40 p-3 text-sm whitespace-pre-line">
                          {generatedFlyers[activeResultId ?? ""]}
                        </div>
                      ) : (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={!!generatingConversion[activeResultId ?? ""]}
                          onClick={() => activeResultId && void generateFlyer(activeResultId)}
                        >
                          {generatingConversion[activeResultId ?? ""] === "flyer" ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Bookmark className="h-4 w-4" />
                          )}
                          Generate flyer
                        </Button>
                      )}
                    </div>

                    <Button
                      size="sm"
                      disabled={!activeResult || !analyzedResultIds.has(activeResult.id)}
                      onClick={addToPackage}
                    >
                      <Plus className="h-4 w-4" /> Add to package
                    </Button>
                  </>
                ) : activeResultId ? (
                  analyzingResultIds.has(activeResultId) ? (
                    <div className="flex items-center gap-2 text-sm text-muted-foreground">
                      <Loader2 className="h-4 w-4 animate-spin" /> Analyzing…
                    </div>
                  ) : (
                    <div className="rounded-xl border border-border bg-background/40 p-4 text-sm text-muted-foreground">
                      Click <strong>Analyze</strong> on the result card to load analysis and conversion tools.
                    </div>
                  )
                ) : (
                  <p className="text-sm text-muted-foreground">Select a result card to load conversion tools.</p>
                )}
              </>
            ) : rightPanelTab === "preview" ? (
              /* ── Preview tab ── */
              <>
                {preview ? (
                  <>
                    <div className="rounded-xl border border-border bg-background/40 p-3">
                      <p className="font-medium">{preview.resultTitle}</p>
                      <p className="mt-1 text-xs text-muted-foreground">
                        Adaptive preview emulation for publication format and social network.
                      </p>
                    </div>

                    {/* Network tabs */}
                    <div className="space-y-2">
                      <p className="text-xs font-medium uppercase tracking-widest text-muted-foreground">
                        Network tabs
                      </p>
                      <div className="flex flex-wrap gap-2">
                        {networkOptions.map((network) => {
                          const Icon = network.icon;
                          return (
                            <Button
                              key={network.value}
                              size="sm"
                              variant={previewNetworkTab === network.value ? "default" : "outline"}
                              onClick={() => handleNetworkTabChange(network.value)}
                            >
                              <Icon className="h-4 w-4" /> {network.label}
                            </Button>
                          );
                        })}
                      </div>
                    </div>

                    {/* Mutation format */}
                    <div className="space-y-2">
                      <label className="text-xs font-medium uppercase tracking-widest text-muted-foreground">
                        Mutation format
                      </label>
                      <Select
                        className="w-full"
                        value={mutationMode}
                        onChange={(event) => setMutationMode(event.target.value)}
                      >
                        {(networkMutationFormats[previewNetworkTab] ?? []).map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </Select>
                    </div>

                    {/* Tablet emulator */}
                    <SocialTabletPreview
                      network={previewNetworkTab}
                      mode={mutationMode}
                      title={preview.resultTitle}
                      summary={activeResult?.summary ?? "Adaptive publication preview"}
                      likes={activeResult?.likes ?? 0}
                      comments={activeResult?.comments ?? 0}
                    />

                    {/* Add to package */}
                    <Button
                      size="sm"
                      disabled={!activeResult || !analyzedResultIds.has(activeResult.id)}
                      onClick={addToPackage}
                    >
                      <Plus className="h-4 w-4" /> Add to package
                    </Button>

                    <div className="rounded-xl border border-border bg-background/40 p-3 text-sm whitespace-pre-line">
                      {preview.scriptDraft}
                    </div>

                    <p className="text-xs text-muted-foreground">
                      Future-ready: this emulator will render client-specific AI-generated videos in real time.
                    </p>
                  </>
                ) : activeResultId ? (
                  <div className="rounded-xl border border-border bg-background/40 p-4 text-sm text-muted-foreground">
                    Click <strong>Analyze</strong> on the result card to load analysis and script for this item.
                  </div>
                ) : (
                  <p className="text-sm text-muted-foreground">Select a result card to load preview tabs.</p>
                )}
              </>
            ) : (
              /* ── Package tab ── */
              <div className="space-y-3">
                {packageCart.length === 0 ? (
                  <p className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground">
                    Add analyzed items from the Conversion tab to build your monthly package.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {packageCart.map((item) => (
                      <div
                        key={item.resultId}
                        className="rounded-xl border border-border bg-background/40 p-3"
                      >
                        {/* Always visible: thumbnail + title + badges + action buttons */}
                        <div className="flex items-start gap-3">
                          <img
                            src={item.thumbnailUrl}
                            alt={item.title}
                            className="h-10 w-[71px] flex-shrink-0 rounded object-cover"
                          />
                          <div className="min-w-0 flex-1">
                            <p className="truncate text-sm font-medium">{item.title}</p>
                            <div className="mt-1 flex flex-wrap gap-1">
                              <Badge variant="muted" className="text-[10px]">
                                {item.networkSource}
                              </Badge>
                              <Badge
                                variant={strategyBadgeVariant(item.strategyMode)}
                                className="text-[10px] uppercase tracking-widest"
                              >
                                {item.strategyMode}
                              </Badge>
                              <Badge variant="muted" className="text-[10px]">
                                {item.mutationFormat}
                              </Badge>
                            </div>
                            {editingCartItemId !== item.resultId && item.notes && (
                              <p className="mt-1 text-xs text-muted-foreground">{item.notes}</p>
                            )}
                          </div>
                          <div className="flex flex-shrink-0 items-center gap-1">
                            {editingCartItemId === item.resultId ? (
                              <>
                                <Button size="sm" className="h-7 px-2" onClick={() => saveCartItemEdit(item.resultId)}>
                                  Save
                                </Button>
                                <Button size="sm" variant="outline" className="h-7 px-2" onClick={() => setEditingCartItemId(null)}>
                                  Cancel
                                </Button>
                              </>
                            ) : (
                              <>
                                <Button
                                  size="sm"
                                  variant="ghost"
                                  className="h-7 px-2"
                                  title="Edit"
                                  onClick={() => startCartItemEdit(item)}
                                >
                                  <Pencil className="h-3.5 w-3.5" />
                                </Button>
                                <Button
                                  size="sm"
                                  variant="ghost"
                                  className="h-7 px-2"
                                  title="Open video"
                                  onClick={() => window.open(item.videoUrl, "_blank", "noreferrer")}
                                >
                                  <ExternalLink className="h-3.5 w-3.5" />
                                </Button>
                                <Button
                                  size="sm"
                                  variant="ghost"
                                  className="h-7 px-2 text-red-400 hover:text-red-300"
                                  title="Remove"
                                  onClick={() => removeFromCart(item.resultId)}
                                >
                                  <Trash2 className="h-3.5 w-3.5" />
                                </Button>
                              </>
                            )}
                          </div>
                        </div>
                        {/* Edit form: only shown when editing */}
                        {editingCartItemId === item.resultId && (
                          <div className="mt-3 space-y-2 border-t border-border pt-3">
                            <Select
                              className="w-full"
                              value={editCartMutation}
                              onChange={(e) => setEditCartMutation(e.target.value)}
                            >
                              {(networkMutationFormats[item.networkSource] ?? []).map((option) => (
                                <option key={option.value} value={option.value}>
                                  {option.label}
                                </option>
                              ))}
                            </Select>
                            <textarea
                              className="w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
                              rows={2}
                              placeholder="Notes…"
                              value={editCartNotes}
                              onChange={(e) => setEditCartNotes(e.target.value)}
                            />
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                )}

                {/* Generate ZIP */}
                <div className="border-t border-border pt-3">
                  <Button
                    disabled={busy || packageCart.length === 0}
                    onClick={() => void generatePackage()}
                  >
                    <Download className="h-4 w-4" /> Generate simulated ZIP
                  </Button>
                  {busy && zipProgress > 0 && zipProgress < 100 && (
                    <div className="mt-2 space-y-1">
                      <div className="h-1.5 overflow-hidden rounded-full bg-muted">
                        <div className="h-full bg-primary transition-all" style={{ width: `${zipProgress}%` }} />
                      </div>
                      <p className="text-xs text-muted-foreground">Packaging… {zipProgress}%</p>
                    </div>
                  )}
                  {generatedPackage ? (
                    <div className="mt-3 rounded-xl border border-border bg-background/40 p-3 text-sm">
                      <p className="font-medium">{generatedPackage.packageFileName}</p>
                      <p className="text-xs text-muted-foreground">
                        Items: {generatedPackage.itemCount} • Status: {generatedPackage.status}
                      </p>
                      <p className="mt-1 text-xs text-muted-foreground">
                        Download URL (simulated): {generatedPackage.downloadUrl}
                      </p>
                    </div>
                  ) : (
                    <p className="mt-2 text-sm text-muted-foreground">
                      ZIP generation is simulated for now. Video file generation remains deferred.
                    </p>
                  )}
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* ── Workflow History ── */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <History className="h-4 w-4 text-primary" /> Workflow history
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {historyItems.length === 0 ? (
            <p className="text-sm text-muted-foreground">No history yet for this organization.</p>
          ) : (
            historyItems.map((item) => {
              const isDownloaded =
                downloadedWorkflowIds.has(item.workflowId) || generatedPackage?.workflowId === item.workflowId;
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
                      {isDownloaded && (
                        <Badge variant="success" className="text-[10px]">
                          Downloaded
                        </Badge>
                      )}
                      <Badge variant={item.status === "completed" ? "success" : "muted"}>{item.status}</Badge>
                      <Button
                        size="sm"
                        variant="outline"
                        title="Re-download package"
                        onClick={() => void redownloadPackage(item.workflowId)}
                      >
                        <Download className="h-4 w-4" />
                      </Button>
                      <Button
                        size="sm"
                        variant={activeWorkflowId === item.workflowId ? "default" : "outline"}
                        onClick={() => void resumeWorkflow(item)}
                      >
                        <Clock3 className="h-4 w-4" />
                        {activeWorkflowId === item.workflowId ? "Active" : "Resume"}
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        className="h-8 px-2"
                        title={isExpanded ? "Collapse action trail" : "Expand action trail"}
                        onClick={() => void toggleHistoryExpand(item)}
                      >
                        {isExpanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                      </Button>
                    </div>
                  </div>

                  {/* Inline action trail */}
                  {isExpanded && (
                    <div className="border-t border-border/60 px-3 pb-3 pt-2">
                      {detail ? (
                        detail.actions.length === 0 ? (
                          <p className="text-xs text-muted-foreground">No actions recorded for this workflow.</p>
                        ) : (
                          <div className="space-y-2">
                            {detail.actions.map((action) => (
                              <div
                                key={action.actionId}
                                className="rounded-lg border border-border/80 bg-background/70 px-3 py-2 text-xs"
                              >
                                <p className="font-medium">
                                  {action.action} • {action.status}
                                </p>
                                <p className="text-muted-foreground">{action.message}</p>
                                <p className="text-muted-foreground">{formatDate(action.executedAtUtc)}</p>
                              </div>
                            ))}
                          </div>
                        )
                      ) : (
                        <div className="flex items-center gap-2 text-xs text-muted-foreground">
                          <Loader2 className="h-3.5 w-3.5 animate-spin" /> Loading action trail…
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
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/60 px-2 py-1">
      <p className="text-[10px] uppercase tracking-widest text-muted-foreground">{label}</p>
      <p className="text-sm font-medium">{value}</p>
    </div>
  );
}

function SocialTabletPreview({
  network,
  mode,
  title,
  summary,
  likes,
  comments
}: {
  network: PreviewNetworkTab;
  mode: string;
  title: string;
  summary: string;
  likes: number;
  comments: number;
}) {
  const allFormats = Object.values(networkMutationFormats).flat();
  const modeLabel = allFormats.find((item) => item.value === mode)?.label ?? mode;
  const isStory = mode.startsWith("story_");
  const isReel = mode === "reel";
  const isCarousel = mode.includes("carousel") || mode === "slideshow";
  const isShort = mode === "short";
  const isCommunity = mode === "community_carousel";
  const isTikTok = network === "tiktok";

  return (
    <div className="rounded-2xl border border-border bg-background/40 p-3">
      <div className="mx-auto w-full max-w-[360px] rounded-[28px] border border-border bg-zinc-950 p-3 shadow-xl">
        <div className="overflow-hidden rounded-[20px] border border-zinc-700 bg-zinc-900">
          {/* Status bar */}
          <div className="flex items-center justify-between border-b border-zinc-700 px-3 py-2 text-[10px] text-zinc-400">
            <span className="font-medium">
              {network === "instagram" ? "Instagram" : network === "tiktok" ? "TikTok" : "YouTube"}
            </span>
            <span>{modeLabel}</span>
          </div>

          {/* Instagram Story */}
          {network === "instagram" && isStory && (
            <div className="relative flex aspect-[9/16] flex-col bg-gradient-to-b from-fuchsia-700/70 to-slate-900 p-3">
              <div className="flex justify-between">
                <div className="flex items-center gap-1.5">
                  <div className="h-6 w-6 rounded-full bg-zinc-300/30" />
                  <span className="text-xs font-semibold text-zinc-100">@client</span>
                </div>
                <span className="text-[10px] text-zinc-300">3h</span>
              </div>
              <div className="mt-3 h-1 w-full overflow-hidden rounded-full bg-zinc-700">
                <div className="h-full w-3/5 bg-white/70" />
              </div>
              <div className="mt-auto space-y-2">
                <div className="h-32 w-full rounded-xl bg-zinc-200/20" />
                <p className="line-clamp-2 text-sm font-semibold text-white">{title}</p>
              </div>
              <div className="mt-3 flex items-center justify-center gap-6 text-zinc-200">
                <Heart className="h-5 w-5" />
                <MessageCircle className="h-5 w-5" />
                <Share2 className="h-5 w-5" />
              </div>
            </div>
          )}

          {/* Instagram Reel */}
          {network === "instagram" && isReel && (
            <div className="relative flex aspect-[9/16] flex-col justify-between bg-gradient-to-b from-zinc-800 to-zinc-950 p-3">
              <div className="flex items-center gap-1.5">
                <div className="h-6 w-6 rounded-full bg-zinc-300/30" />
                <span className="text-xs font-semibold text-zinc-100">@client</span>
                <span className="ml-auto rounded border border-zinc-500 px-2 py-0.5 text-[10px] text-zinc-200">
                  Follow
                </span>
              </div>
              <div className="h-44 w-full rounded-xl bg-zinc-200/20" />
              <div className="space-y-1">
                <p className="line-clamp-2 text-sm font-semibold text-white">{title}</p>
                <p className="line-clamp-2 text-xs text-zinc-300">{summary}</p>
              </div>
              <div className="flex items-center justify-between text-xs text-zinc-300">
                <div className="flex items-center gap-3">
                  <span className="flex items-center gap-1">
                    <Heart className="h-4 w-4" /> {Math.max(likes, 0).toLocaleString()}
                  </span>
                  <span className="flex items-center gap-1">
                    <MessageCircle className="h-4 w-4" /> {Math.max(comments, 0).toLocaleString()}
                  </span>
                </div>
                <Bookmark className="h-4 w-4" />
              </div>
            </div>
          )}

          {/* Instagram Carousel / Feed */}
          {network === "instagram" && isCarousel && (
            <div className="flex aspect-square flex-col justify-between bg-zinc-900 p-3">
              <div className="flex items-center gap-1.5">
                <div className="h-6 w-6 rounded-full bg-zinc-300/30" />
                <span className="text-xs font-semibold text-zinc-100">@client</span>
              </div>
              <div className="grid grid-cols-3 gap-1">
                {Array.from({ length: 3 }).map((_, i) => (
                  <div key={i} className="aspect-square rounded-md bg-zinc-200/20" />
                ))}
              </div>
              <div className="flex justify-center gap-1">
                {Array.from({ length: 3 }).map((_, i) => (
                  <div key={i} className={cn("h-1.5 w-1.5 rounded-full", i === 0 ? "bg-blue-400" : "bg-zinc-600")} />
                ))}
              </div>
              <p className="line-clamp-2 text-xs font-medium text-zinc-100">{title}</p>
              <div className="flex items-center gap-3 text-xs text-zinc-300">
                <span className="flex items-center gap-1">
                  <Heart className="h-4 w-4" /> {Math.max(likes, 0).toLocaleString()}
                </span>
                <span className="flex items-center gap-1">
                  <MessageCircle className="h-4 w-4" /> {Math.max(comments, 0).toLocaleString()}
                </span>
              </div>
            </div>
          )}

          {/* Instagram Feed post (non-carousel) */}
          {network === "instagram" && !isStory && !isReel && !isCarousel && (
            <div className="flex aspect-square flex-col justify-between bg-zinc-900 p-3">
              <div className="flex items-center gap-1.5">
                <div className="h-6 w-6 rounded-full bg-zinc-300/30" />
                <span className="text-xs font-semibold text-zinc-100">@client</span>
              </div>
              <div className="h-40 w-full rounded-xl bg-zinc-200/20" />
              <p className="line-clamp-2 text-xs font-medium text-zinc-100">{title}</p>
              <div className="flex items-center gap-3 text-xs text-zinc-300">
                <span className="flex items-center gap-1">
                  <Heart className="h-4 w-4" /> {Math.max(likes, 0).toLocaleString()}
                </span>
                <span className="flex items-center gap-1">
                  <MessageCircle className="h-4 w-4" /> {Math.max(comments, 0).toLocaleString()}
                </span>
              </div>
            </div>
          )}

          {/* TikTok */}
          {isTikTok && (
            <div className="relative flex aspect-[9/16] flex-col justify-between bg-zinc-950 p-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold text-zinc-100">For You</span>
                <Video className="h-4 w-4 text-zinc-300" />
              </div>
              <div className="h-48 w-full rounded-xl bg-zinc-200/15" />
              <div className="space-y-1">
                <p className="line-clamp-2 text-sm font-semibold text-white">{title}</p>
                <p className="line-clamp-1 text-[10px] text-zinc-400">{summary}</p>
              </div>
              <div className="flex items-end justify-between text-xs text-zinc-300">
                <div className="space-y-2">
                  <div className="flex items-center gap-1">
                    <Heart className="h-4 w-4" /> {Math.max(likes, 0).toLocaleString()}
                  </div>
                  <div className="flex items-center gap-1">
                    <MessageCircle className="h-4 w-4" /> {Math.max(comments, 0).toLocaleString()}
                  </div>
                  <Share2 className="h-4 w-4" />
                </div>
                <div className="h-10 w-10 rounded-full border-2 border-white bg-zinc-400/30" />
              </div>
            </div>
          )}

          {/* YouTube Short */}
          {network === "youtube" && isShort && (
            <div className="relative flex aspect-[9/16] flex-col justify-between bg-zinc-950 p-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-white">Shorts</span>
                <Share2 className="h-4 w-4 text-zinc-300" />
              </div>
              <div className="h-48 w-full rounded-xl bg-zinc-200/15" />
              <div className="space-y-1">
                <p className="line-clamp-2 text-sm font-semibold text-white">{title}</p>
              </div>
              <div className="flex items-center justify-between text-xs text-zinc-300">
                <div className="flex items-center gap-3">
                  <span className="flex items-center gap-1">
                    <Heart className="h-4 w-4" /> {Math.max(likes, 0).toLocaleString()}
                  </span>
                  <span className="flex items-center gap-1">
                    <MessageCircle className="h-4 w-4" /> {Math.max(comments, 0).toLocaleString()}
                  </span>
                </div>
              </div>
            </div>
          )}

          {/* YouTube Community carousel */}
          {network === "youtube" && isCommunity && (
            <div className="flex aspect-[4/3] flex-col justify-between bg-zinc-900 p-3">
              <div className="flex items-center gap-2">
                <div className="h-7 w-7 rounded-full bg-red-500/70" />
                <div>
                  <p className="text-xs font-semibold text-zinc-100">Channel</p>
                  <p className="text-[10px] text-zinc-400">Community post</p>
                </div>
              </div>
              <p className="line-clamp-3 text-xs text-zinc-200">{summary}</p>
              <div className="grid grid-cols-2 gap-1">
                {Array.from({ length: 2 }).map((_, i) => (
                  <div key={i} className="aspect-video rounded-md bg-zinc-200/20" />
                ))}
              </div>
              <div className="flex items-center gap-3 text-xs text-zinc-300">
                <span className="flex items-center gap-1">
                  <Heart className="h-4 w-4" /> {Math.max(likes, 0).toLocaleString()}
                </span>
                <span className="flex items-center gap-1">
                  <MessageCircle className="h-4 w-4" /> {Math.max(comments, 0).toLocaleString()}
                </span>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function formatDate(value: string) {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? "—" : parsed.toLocaleString();
}
