"use client";

import { useDateFormatter } from "@/lib/format/use-date-formatter";
import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { ListChecks, Loader2, Sparkles } from "lucide-react";
import { listOrganizations } from "@/lib/api/organizations";
import { listSocialNetworks } from "@/lib/api/social-networks";
import {
  getContentWorkflowHistory,
  getContentWorkflowStatus,
  listContentWorkflowHistory,
  listContentWorkflowResults,
  listWorkflowClients,
  recoverContentWorkflowCandidate,
  selectContentWorkflowResults,
  startContentWorkflow
} from "@/lib/api/content-workflow";
import {
  ApiError,
  ContentWorkflowHistory,
  ContentWorkflowHistoryItem,
  ContentWorkflowResult,
  ContentWorkflowSearchCriterion,
  ContentWorkflowStatus,
  ContentWorkflowStrategyMode,
  Organization,
  PageResult,
  SocialClient,
  SocialNetwork
} from "@/lib/api/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { useToast } from "@/components/ui/toaster";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { splitCandidatesByProcessedState } from "@/lib/social/workflow-candidate-grouping";
import { useCandidateComposition } from "./hooks/use-candidate-composition";
import { useFormatCatalog } from "./hooks/use-format-catalog";
import { useWorkflowEnqueue } from "./hooks/use-workflow-enqueue";
import { ConfigurationPanel } from "./panels/configuration-panel";
import { GenerationProgressPanel } from "./panels/generation-progress-panel";
import { CandidateGrid } from "./candidates/candidate-grid";
import { FormatCompositionEditor } from "./composition/format-composition-editor";
import { EnqueueSummary } from "./composition/enqueue-summary";
import { WorkflowHistoryPanel } from "./history/workflow-history-panel";

interface WorkflowClientProps {
  isPlatformAdmin: boolean;
}

export function WorkflowClient({ isPlatformAdmin }: WorkflowClientProps) {
  const { formatDateTime: formatDate } = useDateFormatter();
  const token = useAccessToken();
  const toast = useToast();
  const { orgId: activeOrgId, isInitialized: orgInitialized } = useActiveOrg();

  // Organization + Client
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState<string>(() => activeOrgId ?? "");
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
  const [historyClientFilter, setHistoryClientFilter] = useState("");

  // UI state
  const [candidateViewMode, setCandidateViewMode] = useState<"grid" | "video">("video");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(12);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [recoveringCandidateId, setRecoveringCandidateId] = useState<string | null>(null);

  // Collapsible panels — default open for a fresh run, default collapsed when opening a historical
  // record's summary (spec: "Collapsible progress and configuration panels").
  const [configPanelOpen, setConfigPanelOpen] = useState(true);
  const [progressPanelOpen, setProgressPanelOpen] = useState(true);

  // History expand + detail map
  const [expandedHistoryId, setExpandedHistoryId] = useState<string | null>(null);
  const [historyDetailMap, setHistoryDetailMap] = useState<Record<string, ContentWorkflowHistory>>({});

  const composition = useCandidateComposition();
  const enqueueState = useWorkflowEnqueue();
  const formatOptions = useFormatCatalog(token);

  const results = useMemo(() => resultsPage?.items ?? [], [resultsPage]);

  // "Select all" must only pick up candidates still in the active pool — a Processed candidate is hidden
  // from the composition flow until the operator explicitly recovers it.
  const activeResults = useMemo(() => splitCandidatesByProcessedState(results).active, [results]);

  const activeResult = useMemo(
    () => results.find((item) => item.id === activeResultId) ?? null,
    [results, activeResultId]
  );

  const selectedResults = useMemo(
    () => results.filter((item) => selectedResultIds.includes(item.id)),
    [results, selectedResultIds]
  );

  const composedResultIds = useMemo(() => {
    const ids = new Set<string>();
    for (const id of selectedResultIds) {
      if (composition.isComposed(id)) ids.add(id);
    }
    return ids;
  }, [composition, selectedResultIds]);

  const compositionValidation = useMemo(
    () => composition.validate(selectedResultIds),
    [composition, selectedResultIds]
  );

  useEffect(() => {
    if (!orgInitialized) return;
    if (!token) {
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
    if (!selectedOrganizationId || !token) return;

    void loadOrganizationContext(selectedOrganizationId, token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedOrganizationId, token, historyClientFilter]);

  useEffect(() => {
    if (!selectedOrganizationId || !activeWorkflowId || !token) return;

    void loadWorkflowStatus(activeWorkflowId, selectedOrganizationId, token);
    const interval = window.setInterval(() => {
      void loadWorkflowStatus(activeWorkflowId, selectedOrganizationId, token);
    }, 2500);

    return () => window.clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeWorkflowId, selectedOrganizationId, token]);

  useEffect(() => {
    if (!selectedOrganizationId || !activeWorkflowId || !token) return;

    void loadWorkflowResults(activeWorkflowId, selectedOrganizationId, pageNumber, token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeWorkflowId, selectedOrganizationId, pageNumber, token]);

  async function bootstrap(accessToken?: string) {
    setLoading(true);
    setError(null);

    try {
      const networksResult = await listSocialNetworks({ includeInactive: false, pageSize: 100 }, accessToken).catch(() => null);
      if (networksResult) {
        setAvailableNetworks(networksResult.items);
        setNetworkSources(networksResult.items.slice(0, 2).map((n) => n.code));
      }

      if (isPlatformAdmin) {
        const nextOrganizations = (await listOrganizations({ page: 1, pageSize: 100, includeInactive: false }, accessToken!)).items;
        setOrganizations(nextOrganizations);
        setSelectedOrganizationId(nextOrganizations[0]?.id ?? "");
      } else {
        setSelectedOrganizationId(activeOrgId ?? "");
      }
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }

  async function loadOrganizationContext(organizationId: string, accessToken: string) {
    setBusy(true);
    setError(null);

    try {
      const nextClients = (await listWorkflowClients(organizationId, accessToken)).items;
      const nextHistory = await listContentWorkflowHistory(
        organizationId,
        12,
        accessToken,
        historyClientFilter || undefined
      );

      setClients(nextClients);
      setSelectedClientId((current) => current || nextClients[0]?.id || "");
      setHistoryItems(nextHistory);
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setBusy(false);
    }
  }

  async function refreshHistory(organizationId: string, accessToken: string) {
    const history = await listContentWorkflowHistory(organizationId, 12, accessToken, historyClientFilter || undefined);
    setHistoryItems(history);
  }

  async function discoverCandidates() {
    const client = clients.find((item) => item.id === selectedClientId);
    if (!selectedOrganizationId || !client || !token) return;

    setBusy(true);
    setError(null);
    setActiveResultId(null);
    setSelectedResultIds([]);
    composition.reset();
    enqueueState.reset();
    setConfigPanelOpen(true);
    setProgressPanelOpen(true);

    try {
      const payload = {
        clientId: client.id,
        clientName: client.name,
        searchCriterion,
        strategyMode,
        networkSources,
        maxResults
      };
      const session = await startContentWorkflow(selectedOrganizationId, payload, token);

      setActiveWorkflowId(session.workflowId);
      setPageNumber(1);
      toast.success("Discovery started", `Finding candidates for ${client.name}.`);
      await Promise.all([
        loadWorkflowStatus(session.workflowId, selectedOrganizationId, token),
        loadWorkflowResults(session.workflowId, selectedOrganizationId, 1, token),
        refreshHistory(selectedOrganizationId, token)
      ]);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not discover candidates", message);
    } finally {
      setBusy(false);
    }
  }

  async function loadWorkflowStatus(workflowId: string, organizationId: string, accessToken: string) {
    try {
      setStatus(await getContentWorkflowStatus(organizationId, workflowId, accessToken));
    } catch (err) {
      setError((err as ApiError).message);
    }
  }

  async function loadWorkflowResults(workflowId: string, organizationId: string, page: number, accessToken: string) {
    try {
      const pageResult = await listContentWorkflowResults(organizationId, workflowId, page, pageSize, accessToken);
      setResultsPage(pageResult);
    } catch (err) {
      setError((err as ApiError).message);
    }
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
    if (!selectedOrganizationId || !activeWorkflowId || selectedResultIds.length === 0 || !token) return;

    setBusy(true);
    setError(null);

    try {
      const selection = await selectContentWorkflowResults(selectedOrganizationId, activeWorkflowId, selectedResultIds, token);
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

  async function resumeWorkflow(history: ContentWorkflowHistoryItem) {
    if (!selectedOrganizationId || !token) return;
    if (activeWorkflowId === history.workflowId) return;

    setBusy(true);
    setError(null);

    try {
      setActiveWorkflowId(history.workflowId);
      setPageNumber(1);
      setActiveResultId(null);
      setSelectedResultIds([]);
      composition.reset();
      enqueueState.reset();
      setConfigPanelOpen(false);
      setProgressPanelOpen(false);
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

    if (!historyDetailMap[item.workflowId] && selectedOrganizationId && token) {
      try {
        const detail = await getContentWorkflowHistory(selectedOrganizationId, item.workflowId, token);
        setHistoryDetailMap((prev) => ({ ...prev, [item.workflowId]: detail }));
      } catch (err) {
        toast.error("Could not load history", (err as ApiError).message);
      }
    }
  }

  async function handleEnqueue() {
    if (!selectedOrganizationId || !activeWorkflowId || !selectedClientId || !token) return;
    if (!compositionValidation.valid) return;

    const payload = composition.buildPayload(selectedResultIds);
    if (payload.length === 0) return;

    const result = await enqueueState.enqueue(selectedOrganizationId, activeWorkflowId, selectedClientId, payload, token);
    if (result) {
      toast.success("Candidates enqueued", `${payload.length} candidate(s) sent for generation.`);
      await refreshHistory(selectedOrganizationId, token);
    }
  }

  async function handleRecoverCandidate(candidateId: string) {
    if (!selectedOrganizationId || !activeWorkflowId || !token) return;

    setRecoveringCandidateId(candidateId);
    try {
      await recoverContentWorkflowCandidate(selectedOrganizationId, activeWorkflowId, candidateId, token);
      toast.success("Candidate recovered", "It is back in the active candidate pool.");
      await loadWorkflowResults(activeWorkflowId, selectedOrganizationId, pageNumber, token);
    } catch (err) {
      toast.error("Could not recover candidate", (err as ApiError).message);
    } finally {
      setRecoveringCandidateId(null);
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
                Select your client, search criteria, and strategy to discover candidates, then compose per-candidate
                formats and enqueue them for real generation.
              </p>
            </div>
          </div>
          <div className="flex flex-wrap items-end gap-3">
            <Button asChild variant="outline" size="sm">
              <Link href="/workflow/progress">
                <ListChecks className="h-4 w-4" /> View enqueued work
              </Link>
            </Button>
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
          </div>
        </div>
      </section>

      {error ? (
        <div className="rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-100">{error}</div>
      ) : null}

      <ConfigurationPanel
        open={configPanelOpen}
        onOpenChange={setConfigPanelOpen}
        clients={clients}
        selectedClientId={selectedClientId}
        onSelectedClientIdChange={setSelectedClientId}
        searchCriterion={searchCriterion}
        onSearchCriterionChange={setSearchCriterion}
        strategyMode={strategyMode}
        onStrategyModeChange={setStrategyMode}
        maxResults={maxResults}
        onMaxResultsChange={setMaxResults}
        availableNetworks={availableNetworks}
        networkSources={networkSources}
        onToggleNetwork={toggleNetwork}
        busy={busy}
        onDiscover={() => void discoverCandidates()}
      />

      <GenerationProgressPanel
        open={progressPanelOpen}
        onOpenChange={setProgressPanelOpen}
        status={status}
        steps={status?.steps ?? []}
      />

      <div className="grid gap-4 2xl:grid-cols-[1.45fr_1.05fr]">
        <CandidateGrid
          resultsPage={resultsPage}
          pageNumber={pageNumber}
          busy={busy}
          viewMode={candidateViewMode}
          onViewModeChange={setCandidateViewMode}
          activeResultId={activeResultId}
          selectedResultIds={selectedResultIds}
          composedResultIds={composedResultIds}
          availableNetworks={availableNetworks}
          onToggleSelected={toggleSelection}
          onOpenResult={setActiveResultId}
          onSelectAll={() => setSelectedResultIds(activeResults.map((r) => r.id))}
          onDeselectAll={() => setSelectedResultIds([])}
          onSaveSelection={() => void saveSelection()}
          onPreviousPage={() => setPageNumber((current) => Math.max(1, current - 1))}
          onNextPage={() => setPageNumber((current) => current + 1)}
          onRecoverCandidate={(id) => void handleRecoverCandidate(id)}
          recoveringCandidateId={recoveringCandidateId}
        />

        <div className="space-y-4">
          <Card>
            <CardHeader className="border-b border-border">
              <CardTitle className="text-base">Compose formats</CardTitle>
            </CardHeader>
            <CardContent className="p-4">
              <FormatCompositionEditor
                result={activeResult}
                formatOptions={formatOptions}
                getCount={composition.getCount}
                onChangeCount={composition.setCount}
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="border-b border-border">
              <CardTitle className="text-base">Enqueue</CardTitle>
            </CardHeader>
            <CardContent className="p-4">
              <EnqueueSummary
                selectedResults={selectedResults}
                composition={composition.state}
                validation={compositionValidation}
                busy={enqueueState.busy}
                response={enqueueState.response}
                error={enqueueState.error}
                onEnqueue={() => void handleEnqueue()}
                onReset={() => enqueueState.reset()}
              />
            </CardContent>
          </Card>
        </div>
      </div>

      <WorkflowHistoryPanel
        historyItems={historyItems}
        clients={clients}
        historyClientFilter={historyClientFilter}
        onHistoryClientFilterChange={setHistoryClientFilter}
        activeWorkflowId={activeWorkflowId}
        expandedHistoryId={expandedHistoryId}
        historyDetailMap={historyDetailMap}
        formatDate={formatDate}
        onResume={(item) => void resumeWorkflow(item)}
        onToggleExpand={(item) => void toggleHistoryExpand(item)}
      />
    </div>
  );
}
