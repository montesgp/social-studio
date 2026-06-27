"use client";

import { FormEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import {
  DndContext,
  type DragEndEvent,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  Bot,
  BrainCircuit,
  CheckCircle2,
  ChevronDown,
  Cloud,
  DatabaseZap,
  FolderOpen,
  GripVertical,
  KeyRound,
  Link2,
  Music2,
  Plus,
  RefreshCcw,
  Settings2,
  Shield,
  Sparkles,
  Trash2,
  Unplug,
  WandSparkles,
  X,
  Youtube,
  Instagram
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import {
  configureOrganizationIntegration,
  deleteOrganizationIntegration,
  getCanvaAuthorizeUrl,
  getOrganizationIntegration,
  listIntegrationProviders,
  listOrganizationIntegrations,
  listWorkflowConfigurations,
  syncOrganizationAiModels,
  testOrganizationIntegration,
  upsertWorkflowConfigurations
} from "@/lib/api/integrations";
import { listOrganizations } from "@/lib/api/organizations";
import {
  ApiError,
  IntegrationConnectionMode,
  IntegrationProvider,
  IntegrationProviderCategory,
  IntegrationStatus,
  Organization,
  OrganizationIntegration,
  OrganizationIntegrationsOverview,
  SocialNetwork,
  WorkflowCatalog,
  WorkflowConfigurationCatalog,
  WorkflowStepCatalog,
  WorkflowStepConfigurationInput
} from "@/lib/api/types";
import { listSocialNetworks } from "@/lib/api/social-networks";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Select } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/ui/password-input";
import { useToast } from "@/components/ui/toaster";
import { cn } from "@/lib/utils";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { show as showBlockingLoader, hide as hideBlockingLoader } from "@/lib/loading/blocking-loader-store";

const categoryLabels: Record<string, string> = {
  AiModel: "AI model providers",
  Design: "Design providers",
  DataExtraction: "Data providers",
  Publishing: "Publishing providers",
  MediaGeneration: "Media generation providers"
};

const providerIcons: Record<string, LucideIcon> = {
  "google-drive": FolderOpen,
  apify: Cloud,
  data365: DatabaseZap,
  claude: BrainCircuit,
  openai: Bot,
  "genspark-ai": WandSparkles,
  tiktok: Music2,
  youtube: Youtube,
  instagram: Instagram,
  gemini: Sparkles,
  canva: Sparkles,
  blotato: Link2
};

interface ModalState {
  integration: OrganizationIntegration;
  mode: IntegrationConnectionMode;
  enabled: boolean;
  selectedAiModel: string;
  credentials: Record<string, string>;
  apifyActorMappings: ApifyActorMappingDraft[];
}

interface ApifyActorMappingDraft {
  id: string;
  networkKey: string;
  variantKey: string;
  actorId: string;
  isPrimary: boolean;
}

interface WorkflowStepDraft {
  id: string;
  analysisType: string;
  stepKey: string;
  operationKey: string;
  networkKey: string;
  providerKey: string;
  modelId: string;
  actorId: string;
  priority: number;
  isEnabled: boolean;
}

interface WorkflowStageDraft {
  key: string;
  analysisType: string;
  stepKey: string;
  operationKey: string;
  networkKey: string;
  routes: WorkflowStepDraft[];
}

const apifyActorMappingsCredentialKey = "actor_mappings_json";
const APIFY_VARIANT_OPTIONS: { value: string; label: string }[] = [
  { value: "social-extraction", label: "Análisis de contenido" },
  { value: "profile", label: "Análisis de perfil" }
];

interface WorkflowSelectOption {
  value: string;
  label: string;
}

function newApifyActorMapping(defaultNetworkKey: string): ApifyActorMappingDraft {
  return {
    id: crypto.randomUUID(),
    networkKey: defaultNetworkKey,
    variantKey: "social-extraction",
    actorId: "",
    isPrimary: false
  };
}

interface RawActorMapping {
  networkKey?: unknown;
  variantKey?: unknown;
  actorId?: unknown;
  isPrimary?: unknown;
}

function parseApifyActorMappings(value: string | undefined): ApifyActorMappingDraft[] {
  if (!value?.trim()) {
    return [];
  }

  try {
    const parsed = JSON.parse(value) as RawActorMapping[];
    if (!Array.isArray(parsed)) {
      return [];
    }

    const rows = parsed
      .map((item) => {
        const networkKey = typeof item.networkKey === "string" ? item.networkKey.trim() : "";
        const rawVariant = typeof item.variantKey === "string" ? item.variantKey.trim() : "";
        // Legacy entries with variantKey missing or "default" → treat as social-extraction
        const variantKey = rawVariant === "" || rawVariant === "default" ? "social-extraction" : rawVariant;
        const actorId = typeof item.actorId === "string" ? item.actorId.trim() : "";
        const isPrimary = item.isPrimary === true;
        return { id: crypto.randomUUID(), networkKey, variantKey, actorId, isPrimary };
      })
      .filter((item) => item.networkKey.length > 0 && item.actorId.length > 0);

    return rows;
  } catch {
    return [];
  }
}

function normalizePrimaries(mappings: ApifyActorMappingDraft[]): ApifyActorMappingDraft[] {
  // Group by (networkKey, variantKey). Ensure exactly one isPrimary per group.
  // If a group has a primary already, keep only the first one found as primary.
  // If a group has no primary, promote the first row in that group.
  const groupPrimary = new Map<string, string>(); // groupKey → first primary id

  for (const m of mappings) {
    const key = `${m.networkKey}::${m.variantKey}`;
    if (m.isPrimary && !groupPrimary.has(key)) {
      groupPrimary.set(key, m.id);
    }
  }

  return mappings.map((m) => {
    const key = `${m.networkKey}::${m.variantKey}`;
    if (!groupPrimary.has(key)) {
      // No primary assigned for this group — promote the first row of the group
      const firstInGroup = mappings.find((r) => `${r.networkKey}::${r.variantKey}` === key);
      if (firstInGroup) {
        groupPrimary.set(key, firstInGroup.id);
      }
    }
    return { ...m, isPrimary: groupPrimary.get(key) === m.id };
  });
}

function serializeApifyActorMappings(mappings: ApifyActorMappingDraft[]) {
  const normalized = normalizePrimaries(mappings);
  return JSON.stringify(
    normalized
      .filter((item) => item.networkKey.length > 0 && item.actorId.trim().length > 0)
      .map((item) => ({
        networkKey: item.networkKey,
        variantKey: item.variantKey,
        actorId: item.actorId.trim(),
        isPrimary: item.isPrimary
      }))
  );
}

function appendCurrentOption(
  options: WorkflowSelectOption[],
  value: string,
  fallbackLabel: string,
): WorkflowSelectOption[] {
  if (!value.trim() || options.some((option) => option.value === value)) {
    return options;
  }

  return [{ value, label: fallbackLabel }, ...options];
}

function workflowStageKey({
  analysisType,
  stepKey,
  operationKey,
  networkKey
}: Pick<WorkflowStepDraft, "analysisType" | "stepKey" | "operationKey" | "networkKey">) {
  return [analysisType.trim().toLowerCase(), stepKey.trim().toLowerCase(), operationKey.trim().toLowerCase(), networkKey.trim().toLowerCase()].join("::");
}

function resequenceWorkflowRows(rows: WorkflowStepDraft[]) {
  const counters = new Map<string, number>();

  return rows.map((row) => {
    const key = workflowStageKey(row);
    const nextPriority = (counters.get(key) ?? 0) + 1;
    counters.set(key, nextPriority);

    if (row.priority === nextPriority) {
      return row;
    }

    return { ...row, priority: nextPriority };
  });
}

function routeDisplayLabel(priority: number) {
  return priority === 1 ? "Primary route" : `Backup route ${priority - 1}`;
}

function buildWorkflowStages(
  rows: WorkflowStepDraft[],
  workflow: WorkflowCatalog | null,
  networks: WorkflowSelectOption[]
) {
  const grouped = new Map<string, WorkflowStageDraft>();

  for (const row of rows) {
    const key = workflowStageKey(row);
    const current = grouped.get(key);
    if (current) {
      current.routes.push(row);
      continue;
    }

    grouped.set(key, {
      key,
      analysisType: row.analysisType,
      stepKey: row.stepKey,
      operationKey: row.operationKey,
      networkKey: row.networkKey,
      routes: [row]
    });
  }

  const analysisOrder = new Map<string, number>();
  const stepOrder = new Map<string, number>();
  workflow?.analysisTypes.forEach((analysisType, analysisIndex) => {
    analysisOrder.set(analysisType.key, analysisIndex);
    analysisType.steps.forEach((step, stepIndex) => {
      stepOrder.set(`${analysisType.key}::${step.key}`, stepIndex);
    });
  });

  const networkOrder = new Map<string, number>();
  networks.forEach((network, index) => {
    networkOrder.set(network.value, index);
  });

  return Array.from(grouped.values())
    .map((stage) => ({
      ...stage,
      routes: [...stage.routes].sort((left, right) => left.priority - right.priority || left.providerKey.localeCompare(right.providerKey))
    }))
    .sort((left, right) => {
      const analysisDiff = (analysisOrder.get(left.analysisType) ?? Number.MAX_SAFE_INTEGER)
        - (analysisOrder.get(right.analysisType) ?? Number.MAX_SAFE_INTEGER);
      if (analysisDiff !== 0) {
        return analysisDiff;
      }

      const stepDiff = (stepOrder.get(`${left.analysisType}::${left.stepKey}`) ?? Number.MAX_SAFE_INTEGER)
        - (stepOrder.get(`${right.analysisType}::${right.stepKey}`) ?? Number.MAX_SAFE_INTEGER);
      if (stepDiff !== 0) {
        return stepDiff;
      }

      const leftNetworkIndex = left.networkKey ? (networkOrder.get(left.networkKey) ?? Number.MAX_SAFE_INTEGER) : -1;
      const rightNetworkIndex = right.networkKey ? (networkOrder.get(right.networkKey) ?? Number.MAX_SAFE_INTEGER) : -1;
      if (leftNetworkIndex !== rightNetworkIndex) {
        return leftNetworkIndex - rightNetworkIndex;
      }

      return left.key.localeCompare(right.key);
    });
}

interface WorkflowStepGroupDraft {
  key: string;
  analysisType: string;
  stepKey: string;
  operationKey: string;
  networkStages: WorkflowStageDraft[];
}

function workflowStepGroupKey({
  analysisType,
  stepKey,
  operationKey
}: Pick<WorkflowStepDraft, "analysisType" | "stepKey" | "operationKey">) {
  return [analysisType.trim().toLowerCase(), stepKey.trim().toLowerCase(), operationKey.trim().toLowerCase()].join("::");
}

/**
 * Groups per-network stages by their workflow step (analysis type + step + operation), so the UI can
 * render ONE card per step and expose each social network as an independent tab. Every network keeps
 * its own ordered execution routes — adding a backup never bleeds into a sibling network.
 */
function buildWorkflowStepGroups(
  rows: WorkflowStepDraft[],
  workflow: WorkflowCatalog | null,
  networks: WorkflowSelectOption[]
): WorkflowStepGroupDraft[] {
  const stages = buildWorkflowStages(rows, workflow, networks);
  const grouped = new Map<string, WorkflowStepGroupDraft>();

  for (const stage of stages) {
    const key = workflowStepGroupKey(stage);
    const current = grouped.get(key);
    if (current) {
      current.networkStages.push(stage);
      continue;
    }

    grouped.set(key, {
      key,
      analysisType: stage.analysisType,
      stepKey: stage.stepKey,
      operationKey: stage.operationKey,
      networkStages: [stage]
    });
  }

  return Array.from(grouped.values());
}

function ModelSelect({
  models,
  value,
  onChange,
  placeholder,
}: {
  models: string[];
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
}) {
  if (models.length === 0) {
    return (
      <Input value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} />
    );
  }
  return (
    <Select
      className="w-full"
      value={value}
      onChange={(e) => onChange(e.target.value)}
    >
      <option value="">Select model...</option>
      {models.map((m) => (
        <option key={m} value={m}>{m}</option>
      ))}
    </Select>
  );
}

interface IntegrationsSettingsClientProps {
  isPlatformAdmin: boolean;
}

/**
 * Provider keys whose connector needs a one-time ORG OAuth connection (not just client_id/secret).
 * These show a "Connect" button in addition to the credential config. Single extension point —
 * add a provider key here (and its authorize helper) when another OAuth connector is introduced.
 */
const OAUTH_CONNECT_PROVIDERS = new Set<string>(["canva"]);
const SETTINGS_INTEGRATIONS_BLOCKING_LOADER_ID = "settings-integrations.initial-resources";

export function IntegrationsSettingsClient({ isPlatformAdmin }: IntegrationsSettingsClientProps) {
  const token = useAccessToken();
  const toast = useToast();
  const { orgId: activeOrgId, isInitialized: orgInitialized } = useActiveOrg();
  const overviewRequestIdRef = useRef(0);
  const workflowRequestIdRef = useRef(0);
  const workflowActorRequestIdRef = useRef(0);
  const loadedOverviewRef = useRef<string | null>(null);
  const loadedWorkflowRef = useRef<{ organizationId: string; workflowKey: string } | null>(null);

  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [providers, setProviders] = useState<IntegrationProvider[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState<string>("");
  const [overview, setOverview] = useState<OrganizationIntegrationsOverview | null>(null);
  // Organization id the currently loaded `overview` belongs to. Used to keep the blocking loader up
  // until the overview for the SELECTED org has actually arrived — otherwise switching orgs hides the
  // loader as soon as the (fast) workflow load finishes while the (slow) overview is still in flight.
  const [overviewOrgId, setOverviewOrgId] = useState<string | null>(null);
  const [socialNetworks, setSocialNetworks] = useState<SocialNetwork[]>([]);
  const [loading, setLoading] = useState(true);
  // Value intentionally unbound: handlers still flip this to gate concurrent ops, but no UI reads it
  // directly (the former Default-AI save button that did was removed).
  const [, setRefreshing] = useState(false);
  const [manualRefreshing, setManualRefreshing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [workflowCatalog, setWorkflowCatalog] = useState<WorkflowConfigurationCatalog | null>(null);
  const [workflowApifyActors, setWorkflowApifyActors] = useState<ApifyActorMappingDraft[]>([]);
  const [modal, setModal] = useState<ModalState | null>(null);
  const [deleteConfirmIntegration, setDeleteConfirmIntegration] = useState<OrganizationIntegration | null>(null);
  const [syncingModelsProviderKey, setSyncingModelsProviderKey] = useState<string | null>(null);
  const [selectedWorkflowKey, setSelectedWorkflowKey] = useState<string>("single-analyzer");
  const [workflowRows, setWorkflowRows] = useState<WorkflowStepDraft[]>([]);
  const [workflowLoading, setWorkflowLoading] = useState(false);
  const [workflowActorsLoading, setWorkflowActorsLoading] = useState(false);
  const [workflowSaving, setWorkflowSaving] = useState(false);
  // Active network tab per step group (key = workflowStepGroupKey). Pure view state — switching a tab
  // never mutates routes; it only changes which network's execution routes are visible.
  const [activeNetworkByGroup, setActiveNetworkByGroup] = useState<Record<string, string>>({});
  // Collapsed step group cards (key = workflowStepGroupKey). Pure view state so the admin can fold the
  // steps they are not editing ("Resolve source content", "Resolve profile snapshot", …) for clarity.
  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>({});

  const groupedIntegrations = useMemo(() => {
    const map = new Map<string, OrganizationIntegration[]>();
    const items = overview?.integrations ?? [];

    for (const integration of items) {
      const key = integration.category;
      const group = map.get(key) ?? [];
      group.push(integration);
      map.set(key, group);
    }

    return Array.from(map.entries());
  }, [overview?.integrations]);

  const connectedWorkflowIntegrations = useMemo(() => {
    return (overview?.integrations ?? []).filter((integration) => integration.status === "Connected");
  }, [overview?.integrations]);

  const socialNetworkOptions = useMemo(() => {
    return socialNetworks
      .filter((network) => network.isActive)
      .map((network) => ({
        value: network.code,
        label: network.displayName
      }));
  }, [socialNetworks]);

  const workflowDefinitions = useMemo(() => workflowCatalog?.workflows ?? [], [workflowCatalog]);

  // Workflow selector options are DRIVEN BY THE CARDS ABOVE (checklist groups), so every tool the
  // organization can set up — even ones with no published workflow catalog yet (e.g. Content Generator,
  // a future "Magic Generator", …) — appears here. The catalog only governs whether the selected tool
  // is actually configurable; the option list stays dynamic with whatever the overview exposes.
  const workflowOptions = useMemo<WorkflowSelectOption[]>(() => {
    const groups = overview?.checklistGroups ?? [];
    if (groups.length > 0) {
      return groups.map((group) => ({ value: group.key, label: group.label }));
    }

    return workflowDefinitions.map((workflow) => ({ value: workflow.key, label: workflow.label }));
  }, [overview?.checklistGroups, workflowDefinitions]);

  const selectedWorkflowLabel = useMemo(() => {
    return workflowOptions.find((option) => option.value === selectedWorkflowKey)?.label ?? selectedWorkflowKey;
  }, [selectedWorkflowKey, workflowOptions]);

  // Resolved ONLY when the selected tool has a published catalog. A selected card without a catalog
  // (Content Generator today) stays null so the UI renders a "not configurable yet" placeholder instead
  // of silently falling back to another tool's steps.
  const selectedWorkflowDefinition = useMemo<WorkflowCatalog | null>(() => {
    return workflowDefinitions.find((workflow) => workflow.key === selectedWorkflowKey) ?? null;
  }, [selectedWorkflowKey, workflowDefinitions]);

  const workflowStages = useMemo(
    () => buildWorkflowStages(workflowRows, selectedWorkflowDefinition, socialNetworkOptions),
    [selectedWorkflowDefinition, socialNetworkOptions, workflowRows]
  );

  const workflowStepGroups = useMemo(
    () => buildWorkflowStepGroups(workflowRows, selectedWorkflowDefinition, socialNetworkOptions),
    [selectedWorkflowDefinition, socialNetworkOptions, workflowRows]
  );

  const hasConnectedWorkflowApify = useMemo(() => {
    return connectedWorkflowIntegrations.some((integration) => integration.providerKey === "apify");
  }, [connectedWorkflowIntegrations]);

  const workflowRouteSensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
  );

  // True while an organization is selected but its overview has not been loaded yet (e.g. right after
  // switching orgs). Keeps the blocking loader visible until the selected org's data is fully available.
  const isAwaitingOverviewForSelectedOrg =
    Boolean(selectedOrganizationId) && overviewOrgId !== selectedOrganizationId;
  const isBlockingInitialPageResources =
    loading || isAwaitingOverviewForSelectedOrg || workflowLoading || workflowActorsLoading;

  useEffect(() => {
    if (!isBlockingInitialPageResources) {
      hideBlockingLoader(SETTINGS_INTEGRATIONS_BLOCKING_LOADER_ID);
      return;
    }

    return showBlockingLoader(SETTINGS_INTEGRATIONS_BLOCKING_LOADER_ID, {
      title: "Loading tool",
      description: "Fetching data required to render this view...",
      mode: "spinner"
    });
  }, [isBlockingInitialPageResources]);

  useEffect(() => {
    if (!orgInitialized) return;
    if (!token) {
      setLoading(false);
      setOverview(null);
      setOrganizations([]);
      setProviders([]);
      setSocialNetworks([]);
      return;
    }

    void bootstrap(token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, orgInitialized]);

  useEffect(() => {
    if (loading || !token || !selectedOrganizationId) {
      return;
    }

    if (loadedOverviewRef.current === selectedOrganizationId) {
      return;
    }

    void loadOverview(selectedOrganizationId, token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loading, selectedOrganizationId]);

  useEffect(() => {
    if (isPlatformAdmin || !orgInitialized) {
      return;
    }

    const nextOrganizationId = activeOrgId ?? "";
    if (nextOrganizationId !== selectedOrganizationId) {
      changeSelectedOrganization(nextOrganizationId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeOrgId, isPlatformAdmin, orgInitialized, selectedOrganizationId]);

  useEffect(() => {
    if (loading) {
      return;
    }

    if (!isPlatformAdmin || !token || !selectedOrganizationId || !selectedWorkflowKey) {
      setWorkflowRows([]);
      setWorkflowCatalog(null);
      setWorkflowLoading(false);
      return;
    }

    const loadedWorkflow = loadedWorkflowRef.current;
    if (
      loadedWorkflow?.organizationId === selectedOrganizationId &&
      loadedWorkflow.workflowKey === selectedWorkflowKey
    ) {
      return;
    }

    void loadWorkflowConfiguration(selectedOrganizationId, selectedWorkflowKey, token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isPlatformAdmin, loading, selectedOrganizationId, selectedWorkflowKey, token]);

  useEffect(() => {
    if (!workflowOptions.length) {
      return;
    }

    if (!workflowOptions.some((option) => option.value === selectedWorkflowKey)) {
      setSelectedWorkflowKey(workflowOptions[0].value);
    }
  }, [selectedWorkflowKey, workflowOptions]);

  useEffect(() => {
    if (!isPlatformAdmin || !token || !selectedOrganizationId || !hasConnectedWorkflowApify) {
      workflowActorRequestIdRef.current += 1;
      setWorkflowApifyActors([]);
      setWorkflowActorsLoading(false);
      return;
    }

    const requestId = ++workflowActorRequestIdRef.current;
    setWorkflowActorsLoading(true);

    void getOrganizationIntegration(selectedOrganizationId, "apify", token)
      .then((integration) => {
        if (requestId !== workflowActorRequestIdRef.current) {
          return;
        }

        setWorkflowApifyActors(
          parseApifyActorMappings(integration.credentialValues?.[apifyActorMappingsCredentialKey])
        );
      })
      .catch((err) => {
        if (requestId !== workflowActorRequestIdRef.current) {
          return;
        }

        const message = (err as ApiError).message;
        setWorkflowApifyActors([]);
        setError(message);
        toast.error("Could not load Apify actor routing", message);
      })
      .finally(() => {
        if (requestId === workflowActorRequestIdRef.current) {
          setWorkflowActorsLoading(false);
        }
      });
  }, [hasConnectedWorkflowApify, isPlatformAdmin, selectedOrganizationId, toast, token]);

  async function bootstrap(accessToken: string) {
    setLoading(true);
    setError(null);

    try {
      if (isPlatformAdmin) {
        const [providerCatalog, organizationsPage] = await Promise.all([
          listIntegrationProviders(accessToken),
          listOrganizations({ page: 1, pageSize: 100, includeInactive: false }, accessToken)
        ]);

        setProviders(providerCatalog);
        setOrganizations(organizationsPage.items);

        const nextOrganizationId = selectedOrganizationId || organizationsPage.items[0]?.id || "";
        setSelectedOrganizationId(nextOrganizationId);

        if (nextOrganizationId) {
          await loadOverview(nextOrganizationId, accessToken, false);
          await loadWorkflowConfiguration(nextOrganizationId, selectedWorkflowKey, accessToken, false);
        } else {
          setOverview(null);
          setWorkflowCatalog(null);
          setWorkflowRows([]);
        }
      } else {
        const providerCatalog = await listIntegrationProviders(accessToken);
        setProviders(providerCatalog);

        const nextOrganizationId = activeOrgId ?? "";
        setSelectedOrganizationId(nextOrganizationId);

        if (nextOrganizationId) {
          await loadOverview(nextOrganizationId, accessToken, false);
        } else {
          setOverview(null);
        }
      }
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }

  async function loadOverview(organizationId: string, accessToken: string, useRefreshState = true) {
    if (!organizationId) {
      return;
    }

    const requestId = ++overviewRequestIdRef.current;

    if (useRefreshState) {
      setRefreshing(true);
    }

    setError(null);

    try {
      const [data, networksPage] = await Promise.all([
        listOrganizationIntegrations(organizationId, accessToken),
        listSocialNetworks({ page: 1, pageSize: 100, includeInactive: false }, accessToken)
      ]);
      const activeNetworks = networksPage.items.filter((network) => network.isActive);
      if (requestId !== overviewRequestIdRef.current) {
        return;
      }

      setOverview(data);
      setSocialNetworks(activeNetworks);
    } catch (err) {
      if (requestId !== overviewRequestIdRef.current) {
        return;
      }

      setError((err as ApiError).message);
    } finally {
      if (requestId === overviewRequestIdRef.current) {
        loadedOverviewRef.current = organizationId;
        // Mark this org's overview as resolved (success OR failure) so the blocking loader is released
        // and the page can render its data — or its error — instead of hanging on the spinner.
        setOverviewOrgId(organizationId);
      }

      if (useRefreshState && requestId === overviewRequestIdRef.current) {
        setRefreshing(false);
      }
    }
  }

  function changeSelectedOrganization(organizationId: string) {
    overviewRequestIdRef.current += 1;
    workflowRequestIdRef.current += 1;
    workflowActorRequestIdRef.current += 1;
    loadedOverviewRef.current = null;
    loadedWorkflowRef.current = null;
    setSelectedOrganizationId(organizationId);
    setOverview(null);
    setOverviewOrgId(null);
    setWorkflowCatalog(null);
    setModal(null);
    setWorkflowRows([]);
    setWorkflowApifyActors([]);
    setWorkflowActorsLoading(false);
    setError(null);
    setRefreshing(false);
  }

  async function refreshOverview() {
    if (!selectedOrganizationId || !token || manualRefreshing) {
      return;
    }

    setManualRefreshing(true);

    try {
      await loadOverview(selectedOrganizationId, token);
    } finally {
      setManualRefreshing(false);
    }
  }

  function modelsForProvider(providerKey: string, category?: IntegrationProviderCategory) {
    if (category) {
      // A shared connector (e.g. OpenAI) is exploded into one card per category; pick the card —
      // and therefore the model menu — that matches the routed category.
      const scopedIntegration = overview?.integrations.find(
        (integration) => integration.providerKey === providerKey && integration.category === category
      );
      if (scopedIntegration) {
        return scopedIntegration.availableModels;
      }

      const provider = providers.find((p) => p.key === providerKey);
      return provider?.modelsByCategory?.[category] ?? provider?.availableModels ?? [];
    }

    return overview?.integrations.find((integration) => integration.providerKey === providerKey)?.availableModels
      ?? providers.find((provider) => provider.key === providerKey)?.availableModels
      ?? [];
  }

  function providerCategory(providerKey: string) {
    return providers.find((provider) => provider.key === providerKey)?.category
      ?? overview?.integrations.find((integration) => integration.providerKey === providerKey)?.category;
  }

  function providerServesCategory(providerKey: string, category: IntegrationProviderCategory) {
    const provider = providers.find((p) => p.key === providerKey);
    if (provider?.categories?.length) {
      return provider.categories.includes(category);
    }

    return (overview?.integrations ?? []).some(
      (integration) => integration.providerKey === providerKey && integration.category === category
    );
  }

  // The category a provider is routed under for a given step: the first allowed category the
  // provider actually serves. Lets a shared connector resolve to image models on a media step and
  // chat models on an AI step.
  function stepCategoryForProvider(
    providerKey: string,
    step?: WorkflowStepCatalog | null
  ): IntegrationProviderCategory | undefined {
    const allowed = step?.allowedProviderCategories ?? [];
    return allowed.find((category) => providerServesCategory(providerKey, category)) ?? providerCategory(providerKey);
  }

  function isAiModelStep(step?: WorkflowStepCatalog | null) {
    return (step?.allowedProviderCategories ?? []).includes("AiModel");
  }

  function providerDisplayName(providerKey: string) {
    return overview?.integrations.find((integration) => integration.providerKey === providerKey)?.providerDisplayName
      ?? providers.find((provider) => provider.key === providerKey)?.displayName
      ?? providerKey;
  }

  function analysisTypeDefinition(analysisType: string, workflow = selectedWorkflowDefinition) {
    return workflow?.analysisTypes.find((item) => item.key === analysisType);
  }

  function stepDefinition(
    analysisType: string,
    stepKey: string,
    workflow = selectedWorkflowDefinition,
  ) {
    return analysisTypeDefinition(analysisType, workflow)?.steps.find((item) => item.key === stepKey);
  }

  function providerOptionsForStep(step?: WorkflowStepCatalog | null) {
    if (!step) {
      return connectedWorkflowIntegrations;
    }

    return connectedWorkflowIntegrations.filter((integration) => {
      const matchesCategory = step.allowedProviderCategories.length === 0
        || step.allowedProviderCategories.includes(integration.category as IntegrationProviderCategory);
      const matchesKey = step.allowedProviderKeys.length === 0
        || step.allowedProviderKeys.includes(integration.providerKey);

      return matchesCategory && matchesKey;
    });
  }

  function networkOptionsForStep(step?: WorkflowStepCatalog | null): WorkflowSelectOption[] {
    if (!step?.requiresNetwork) {
      return [];
    }

    if (step.supportedNetworkKeys.length === 0) {
      return socialNetworkOptions;
    }

    return socialNetworkOptions.filter((option) => step.supportedNetworkKeys.includes(option.value));
  }

  // Network tabs = active+supported networks PLUS any already-configured network that is no longer active
  // (shown as "inactive" so the admin can still review/remove its routes instead of losing them silently).
  function networkTabsForGroup(group: WorkflowStepGroupDraft, step?: WorkflowStepCatalog | null): WorkflowSelectOption[] {
    const base = networkOptionsForStep(step);
    if (!step?.requiresNetwork) {
      return base;
    }

    const known = new Set(base.map((option) => option.value));
    const extra: WorkflowSelectOption[] = [];
    for (const stage of group.networkStages) {
      if (stage.networkKey && !known.has(stage.networkKey)) {
        known.add(stage.networkKey);
        extra.push({ value: stage.networkKey, label: `${stage.networkKey} (inactive)` });
      }
    }

    return [...base, ...extra];
  }

  function activeNetworkForGroup(group: WorkflowStepGroupDraft, step?: WorkflowStepCatalog | null): string {
    if (!step?.requiresNetwork) {
      return "";
    }

    const options = networkTabsForGroup(group, step);
    const chosen = activeNetworkByGroup[group.key];
    if (chosen && options.some((option) => option.value === chosen)) {
      return chosen;
    }

    const firstWithRoutes = group.networkStages.find(
      (stage) => stage.routes.length > 0 && options.some((option) => option.value === stage.networkKey)
    );
    if (firstWithRoutes) {
      return firstWithRoutes.networkKey;
    }

    return options[0]?.value ?? group.networkStages[0]?.networkKey ?? "";
  }

  function modelOptionsForProvider(
    providerKey: string,
    currentValue = "",
    step?: WorkflowStepCatalog | null
  ) {
    const category = step ? stepCategoryForProvider(providerKey, step) : undefined;
    const options = modelsForProvider(providerKey, category).map((modelId) => ({
      value: modelId,
      label: modelId
    }));

    return appendCurrentOption(options, currentValue, `${currentValue} (current route)`);
  }

  function actorOptionsForStep(step?: WorkflowStepCatalog | null, networkKey?: string, currentValue = "") {
    if (!step?.supportsActorSelection || !step.actorVariantKey || !networkKey) {
      return appendCurrentOption([], currentValue, `${currentValue} (current route)`);
    }

    const options = workflowApifyActors
      .filter((mapping) => mapping.networkKey === networkKey && mapping.variantKey === step.actorVariantKey)
      .map((mapping) => ({
        value: mapping.actorId,
        label: mapping.isPrimary ? `${mapping.actorId} (primary)` : `${mapping.actorId} (fallback)`
      }));

    return appendCurrentOption(options, currentValue, `${currentValue} (not in connector)`);
  }

  function normalizeWorkflowRow(seed?: Partial<WorkflowStepDraft>): WorkflowStepDraft {
    const workflow = selectedWorkflowDefinition ?? workflowDefinitions[0] ?? null;
    const analysis = workflow?.analysisTypes.find((item) => item.key === seed?.analysisType)
      ?? workflow?.analysisTypes[0]
      ?? null;
    const step = analysis?.steps.find((item) => item.key === seed?.stepKey)
      ?? analysis?.steps[0]
      ?? null;
    const allowedProviders = providerOptionsForStep(step);
    const providerKey = allowedProviders.some((integration) => integration.providerKey === seed?.providerKey)
      ? seed?.providerKey ?? ""
      : allowedProviders[0]?.providerKey ?? seed?.providerKey ?? "";
    const allowedNetworks = networkOptionsForStep(step);
    const networkKey = step?.requiresNetwork
      ? allowedNetworks.some((network) => network.value === seed?.networkKey)
        ? seed?.networkKey ?? ""
        : allowedNetworks[0]?.value ?? seed?.networkKey ?? ""
      : "";
    const stepCategory = stepCategoryForProvider(providerKey, step);
    const shouldKeepModel = Boolean(step?.supportsModelSelection)
      && (stepCategory === "AiModel" || stepCategory === "MediaGeneration");
    const shouldKeepActor = step?.supportsActorSelection && providerKey === "apify";

    return {
      id: seed?.id ?? crypto.randomUUID(),
      analysisType: analysis?.key ?? seed?.analysisType ?? "",
      stepKey: step?.key ?? seed?.stepKey ?? "",
      operationKey: step?.operationKey ?? seed?.operationKey ?? "",
      networkKey,
      providerKey,
      modelId: shouldKeepModel ? seed?.modelId ?? "" : "",
      actorId: shouldKeepActor ? seed?.actorId ?? "" : "",
      priority: seed?.priority ?? 1,
      isEnabled: seed?.isEnabled ?? true
    };
  }

  function newWorkflowRow(seed?: Partial<WorkflowStepDraft>): WorkflowStepDraft {
    return normalizeWorkflowRow(seed);
  }

  async function loadWorkflowConfiguration(
    organizationId: string,
    workflowKey: string,
    accessToken: string,
    useLoadingState = true
  ) {
    const requestId = ++workflowRequestIdRef.current;
    if (useLoadingState) {
      setWorkflowLoading(true);
    }
    setError(null);

    try {
      const data = await listWorkflowConfigurations(organizationId, workflowKey, accessToken);
      if (requestId !== workflowRequestIdRef.current) {
        return;
      }

      setWorkflowCatalog(data.catalog);
      setWorkflowRows(
        resequenceWorkflowRows(data.steps.map((step) => ({
          id: step.id,
          analysisType: step.analysisType,
          stepKey: step.stepKey,
          operationKey: step.operationKey ?? "",
          networkKey: step.networkKey ?? "",
          providerKey: step.providerKey,
          modelId: step.modelId ?? "",
          actorId: step.actorId ?? "",
          priority: step.priority,
          isEnabled: step.isEnabled
        })))
      );
    } catch (err) {
      if (requestId !== workflowRequestIdRef.current) {
        return;
      }

      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not load workflow configuration", message);
    } finally {
      if (requestId === workflowRequestIdRef.current) {
        loadedWorkflowRef.current = { organizationId, workflowKey };
        if (useLoadingState) {
          setWorkflowLoading(false);
        }
      }
    }
  }

  function addWorkflowStage(seed?: Partial<WorkflowStepDraft>) {
    setWorkflowRows((rows) => resequenceWorkflowRows([...rows, newWorkflowRow(seed)]));
  }

  function seedWorkflowRows() {
    if (!selectedWorkflowDefinition) {
      return;
    }

    const existingStepKeys = new Set(
      workflowRows.map((row) => `${row.analysisType}::${row.stepKey}`.toLowerCase())
    );

    const missingRows = selectedWorkflowDefinition.analysisTypes.flatMap((analysisType) =>
      analysisType.steps
        .filter((step) => !existingStepKeys.has(`${analysisType.key}::${step.key}`.toLowerCase()))
        .map((step) =>
          newWorkflowRow({
            analysisType: analysisType.key,
            stepKey: step.key,
            operationKey: step.operationKey
          })
        )
    );

    if (missingRows.length === 0) {
      toast.success("Workflow already seeded", "All known steps for this workflow are already present.");
      return;
    }

    setWorkflowRows((rows) => resequenceWorkflowRows([...rows, ...missingRows]));
  }

  function patchWorkflowRow(id: string, patch: Partial<WorkflowStepDraft>) {
    setWorkflowRows((rows) =>
      resequenceWorkflowRows(rows.map((row) => {
        if (row.id !== id) {
          return row;
        }

        const next = { ...row, ...patch };
        if (patch.providerKey && patch.providerKey !== row.providerKey) {
          next.modelId = "";
          next.actorId = "";
        }
        if (patch.networkKey && patch.networkKey !== row.networkKey) {
          next.actorId = "";
        }
        return normalizeWorkflowRow(next);
      }))
    );
  }

  function removeWorkflowRow(id: string) {
    setWorkflowRows((rows) => resequenceWorkflowRows(rows.filter((row) => row.id !== id)));
  }

  // Edits analysis type / step for the WHOLE step group (every network keeps its own routes, they are
  // only re-normalized against the new step definition). The network is NOT touched here.
  function patchWorkflowStepGroup(groupKey: string, patch: Partial<WorkflowStepDraft>) {
    setWorkflowRows((rows) =>
      resequenceWorkflowRows(rows.map((row) => {
        if (workflowStepGroupKey(row) !== groupKey) {
          return row;
        }

        return normalizeWorkflowRow({ ...row, ...patch });
      }))
    );
  }

  // Adds a route to a SINGLE network of a step group. Used both for the first route of a network and
  // for backups — priority is derived afterwards by resequenceWorkflowRows (per analysis/step/network).
  function addWorkflowRouteForNetwork(groupKey: string, networkKey: string) {
    setWorkflowRows((rows) => {
      const template = rows.find((row) => workflowStepGroupKey(row) === groupKey);
      if (!template) {
        return rows;
      }

      const route = newWorkflowRow({
        analysisType: template.analysisType,
        stepKey: template.stepKey,
        operationKey: template.operationKey,
        networkKey,
        modelId: "",
        actorId: "",
        isEnabled: true
      });

      return resequenceWorkflowRows([...rows, route]);
    });
  }

  // Removes every route of a single network within a step group. If it was the last network with routes,
  // the whole group card disappears (it is derived from the rows).
  function removeWorkflowNetworkStage(groupKey: string, networkKey: string) {
    setWorkflowRows((rows) =>
      resequenceWorkflowRows(
        rows.filter((row) => !(workflowStepGroupKey(row) === groupKey && row.networkKey === networkKey))
      )
    );
  }

  function reorderWorkflowStageRoutes(stageKey: string, orderedIds: string[]) {
    setWorkflowRows((rows) => {
      const reorderedStageRows = orderedIds
        .map((id) => rows.find((row) => row.id === id && workflowStageKey(row) === stageKey))
        .filter((row): row is WorkflowStepDraft => Boolean(row));

      if (reorderedStageRows.length === 0) {
        return rows;
      }

      let reorderIndex = 0;
      return resequenceWorkflowRows(
        rows.map((row) => (
          workflowStageKey(row) === stageKey
            ? reorderedStageRows[reorderIndex++] ?? row
            : row
        ))
      );
    });
  }

  function handleWorkflowRouteDragEnd(stageKey: string, stage: WorkflowStageDraft, event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) {
      return;
    }

    const currentIds = stage.routes.map((route) => route.id);
    const oldIndex = currentIds.indexOf(String(active.id));
    const newIndex = currentIds.indexOf(String(over.id));
    if (oldIndex < 0 || newIndex < 0) {
      return;
    }

    reorderWorkflowStageRoutes(stageKey, arrayMove(currentIds, oldIndex, newIndex));
  }

  async function saveWorkflowConfiguration() {
    if (!selectedOrganizationId || !selectedWorkflowKey || !token) {
      return;
    }

    const orderedRows = resequenceWorkflowRows(workflowRows);

    const invalid = orderedRows.find(
      (row) =>
        !row.analysisType.trim()
        || !row.stepKey.trim()
        || !row.operationKey.trim()
        || !row.providerKey.trim()
        || row.priority <= 0
    );
    if (invalid) {
      toast.error(
        "Workflow configuration incomplete",
        "Each stage needs a valid workflow step and at least one connected route. Execution order is assigned automatically from the drag-and-drop list."
      );
      return;
    }

    const invalidProvider = orderedRows.find((row) => {
      const step = stepDefinition(row.analysisType, row.stepKey);
      return step ? !providerOptionsForStep(step).some((integration) => integration.providerKey === row.providerKey) : true;
    });
    if (invalidProvider) {
      toast.error(
        "Provider not allowed",
        "Each step must use a connected provider compatible with the selected workflow step."
      );
      return;
    }

    const networkRequired = orderedRows.find((row) => {
      const step = stepDefinition(row.analysisType, row.stepKey);
      return step?.requiresNetwork && !row.networkKey.trim();
    });
    if (networkRequired) {
      toast.error("Network required", "Select an active social network for each extraction step.");
      return;
    }

    const aiWithoutModel = orderedRows.find(
      (row) => {
        const step = stepDefinition(row.analysisType, row.stepKey);
        // Only AI model steps require an explicit model; media generation steps treat it as optional.
        return Boolean(step?.supportsModelSelection)
          && isAiModelStep(step)
          && providerServesCategory(row.providerKey, "AiModel")
          && !row.modelId.trim();
      }
    );
    if (aiWithoutModel) {
      toast.error(
        "AI model required",
        "AI workflow rows need an explicit model synchronized for that organization/provider."
      );
      return;
    }

    const apifyWithoutActor = orderedRows.find(
      (row) => {
        const step = stepDefinition(row.analysisType, row.stepKey);
        return row.providerKey === "apify" && Boolean(step?.supportsActorSelection) && !row.actorId.trim();
      }
    );
    if (apifyWithoutActor) {
      toast.error("Apify actor required", "Apify workflow rows need an actor id for routing and fallbacks.");
      return;
    }

    const steps: WorkflowStepConfigurationInput[] = orderedRows.map((row) => ({
      analysisType: row.analysisType.trim(),
      stepKey: row.stepKey.trim(),
      operationKey: row.operationKey.trim() || null,
      networkKey: row.networkKey.trim() || null,
      providerKey: row.providerKey.trim(),
      modelId: row.modelId.trim() || null,
      actorId: row.actorId.trim() || null,
      priority: Number(row.priority),
      isEnabled: row.isEnabled
    }));

    setWorkflowSaving(true);
    setError(null);

    try {
      const saved = await upsertWorkflowConfigurations(
        selectedOrganizationId,
        selectedWorkflowKey,
        steps,
        token
      );
      setWorkflowRows(
        resequenceWorkflowRows(saved.steps.map((step) => ({
          id: step.id,
          analysisType: step.analysisType,
          stepKey: step.stepKey,
          operationKey: step.operationKey ?? "",
          networkKey: step.networkKey ?? "",
          providerKey: step.providerKey,
          modelId: step.modelId ?? "",
          actorId: step.actorId ?? "",
          priority: step.priority,
          isEnabled: step.isEnabled
        })))
      );
      toast.success("Workflow configuration saved", "The next workflow execution will use these routes.");
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not save workflow configuration", message);
    } finally {
      setWorkflowSaving(false);
    }
  }

  async function runModelSync(integration: OrganizationIntegration) {
    if (!selectedOrganizationId || !token) {
      return;
    }

    setSyncingModelsProviderKey(integration.providerKey);
    setError(null);

    try {
      const result = await syncOrganizationAiModels(selectedOrganizationId, integration.providerKey, token);
      toast.success(
        "Models synchronized",
        `${integration.providerDisplayName}: ${result.models.length} available model(s).`
      );
      await loadOverview(selectedOrganizationId, token, false);
      if (modal?.integration.providerKey === integration.providerKey) {
        const detail = await getOrganizationIntegration(selectedOrganizationId, integration.providerKey, token);
        setModal((prev) => (prev ? { ...prev, integration: detail } : prev));
      }
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not synchronize models", message);
    } finally {
      setSyncingModelsProviderKey(null);
    }
  }

  function addApifyActorMapping() {
    const defaultNetworkKey = socialNetworkOptions[0]?.value ?? "";
    if (!defaultNetworkKey) {
      return;
    }

    setModal((prev) =>
      prev
        ? {
            ...prev,
            apifyActorMappings: [...prev.apifyActorMappings, newApifyActorMapping(defaultNetworkKey)]
          }
        : prev
    );
  }

  function patchApifyActorMapping(id: string, patch: Partial<ApifyActorMappingDraft>) {
    setModal((prev) =>
      prev
        ? {
            ...prev,
            apifyActorMappings: prev.apifyActorMappings.map((item) =>
              item.id === id ? { ...item, ...patch } : item
            )
          }
        : prev
    );
  }

  function removeApifyActorMapping(id: string) {
    setModal((prev) =>
      prev
        ? {
            ...prev,
            apifyActorMappings: prev.apifyActorMappings.filter((item) => item.id !== id)
          }
        : prev
    );
  }

  function setPrimaryActorMapping(id: string) {
    setModal((prev) => {
      if (!prev) return prev;
      const target = prev.apifyActorMappings.find((m) => m.id === id);
      if (!target) return prev;
      const groupKey = `${target.networkKey}::${target.variantKey}`;
      return {
        ...prev,
        apifyActorMappings: prev.apifyActorMappings.map((m) => {
          const mKey = `${m.networkKey}::${m.variantKey}`;
          if (mKey !== groupKey) return m;
          return { ...m, isPrimary: m.id === id };
        })
      };
    });
  }

  async function openConfigure(integration: OrganizationIntegration) {
    if (!selectedOrganizationId || !token) {
      return;
    }

    setRefreshing(true);
    setError(null);

    try {
      const detail = await getOrganizationIntegration(selectedOrganizationId, integration.providerKey, token);
      const mode = detail.isReadOnly
        ? detail.supportsBringYourOwnKey
          ? "BringYourOwnKey"
          : detail.supportsManagedByIncoders
            ? "ManagedByIncoders"
            : detail.connectionMode
        : detail.connectionMode;

      setModal({
        integration: detail,
        mode,
        enabled: detail.status !== "Disabled",
        selectedAiModel: detail.selectedAiModel ?? "",
        credentials: detail.credentialValues ?? {},
        apifyActorMappings: parseApifyActorMappings(detail.credentialValues?.[apifyActorMappingsCredentialKey])
      });
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not load integration", message);
    } finally {
      setRefreshing(false);
    }
  }

  async function saveConfiguration(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!modal || !selectedOrganizationId || !token) {
      return;
    }

    setSaving(true);
    setError(null);

    try {
      const credentialEntries = Object.entries(modal.credentials)
        .filter(([key]) => key !== apifyActorMappingsCredentialKey)
        .filter(([, value]) => value.trim().length > 0)
        .map(([key, value]) => ({ key, value }));

      if (modal.integration.providerKey === "apify") {
        credentialEntries.push({
          key: apifyActorMappingsCredentialKey,
          value: serializeApifyActorMappings(modal.apifyActorMappings)
        });
      }

      await configureOrganizationIntegration(
        selectedOrganizationId,
        modal.integration.providerKey,
        {
          connectionMode: modal.mode,
          enabled: modal.enabled,
          selectedAiModel: modal.selectedAiModel || undefined,
          credentials: credentialEntries
        },
        token
      );

      toast.success("Integration saved", `${modal.integration.providerDisplayName} configuration updated.`);
      setModal(null);
      await loadOverview(selectedOrganizationId, token);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not save integration", message);
    } finally {
      setSaving(false);
    }
  }

  async function runTest(integration: OrganizationIntegration) {
    if (!selectedOrganizationId || !token) {
      return;
    }

    setRefreshing(true);
    setError(null);

    try {
      await testOrganizationIntegration(selectedOrganizationId, integration.providerKey, token);
      toast.success("Connection tested", `${integration.providerDisplayName} test completed.`);
      await loadOverview(selectedOrganizationId, token, false);
    } catch (err) {
      const message = (err as ApiError).message;
      toast.error("Connection test failed", message);
    } finally {
      setRefreshing(false);
    }
  }

  // Starts the ORG-level OAuth connect for connectors that need it (Canva today). One admin
  // connects the org's account once; afterwards every user reuses that connection. The redirect URI
  // is derived from the current origin (local → local, prod → prod) so nothing is hard-coded; it
  // must be in the connector app's allowed redirect URIs.
  async function startOAuthConnect(integration: OrganizationIntegration) {
    if (!selectedOrganizationId || !token || !integration.integrationId) {
      return;
    }

    setError(null);
    try {
      // Canva requires localhost redirect URIs to use 127.0.0.1 (not "localhost"). Normalize only
      // the local host; prod origins pass through unchanged — still fully env-derived, no config.
      const popupOrigin = window.location.origin.replace("://localhost", "://127.0.0.1");
      const redirectUri = `${popupOrigin}/hive-api/api/integrations/${integration.providerKey}/callback`;
      // Provider dispatch — extend here when another OAuth connector is added.
      const { authorizeUrl } = await getCanvaAuthorizeUrl(
        selectedOrganizationId,
        integration.integrationId,
        redirectUri,
        token
      );

      // OAuth in a popup (like "Sign in with Google"): the main app never navigates away, so the
      // session stays intact even though the OAuth round-trip happens on the 127.0.0.1 origin. The
      // popup's result page postMessages the outcome back here; then we refresh the integrations.
      const popup = window.open(authorizeUrl, "canva-oauth-connect", "popup=yes,width=620,height=780");
      if (!popup) {
        // Popups blocked — fall back to a full-page redirect.
        window.location.href = authorizeUrl;
        return;
      }

      // Held in a const ref-object so the listener and the closed-poller can share/clear it.
      const timer: { id?: ReturnType<typeof setInterval> } = {};
      const onMessage = (event: MessageEvent) => {
        if (event.origin !== popupOrigin) {
          return;
        }
        const data = event.data as { type?: string; status?: string; reason?: string };
        if (data?.type !== "canva-oauth-result") {
          return;
        }
        window.removeEventListener("message", onMessage);
        if (timer.id) {
          clearInterval(timer.id);
        }
        try {
          popup.close();
        } catch {
          // ignore
        }
        if (data.status === "connected") {
          toast.success(
            "Canva connected",
            `${integration.providerDisplayName} is now connected for the organization.`
          );
          if (selectedOrganizationId && token) {
            void loadOverview(selectedOrganizationId, token, false);
          }
        } else {
          setError(`Canva connection failed${data.reason ? `: ${data.reason}` : ""}.`);
        }
      };

      window.addEventListener("message", onMessage);
      // Clean up the listener if the user closes the popup without completing.
      timer.id = setInterval(() => {
        if (popup.closed) {
          window.removeEventListener("message", onMessage);
          if (timer.id) {
            clearInterval(timer.id);
          }
        }
      }, 800);
    } catch (err) {
      setError((err as ApiError).message ?? "Failed to start the connection.");
    }
  }

  async function removeConfiguration(integration: OrganizationIntegration) {
    if (!selectedOrganizationId || !token) {
      return;
    }

    setRefreshing(true);
    setError(null);

    try {
      await deleteOrganizationIntegration(selectedOrganizationId, integration.providerKey, token);
      toast.success("Integration removed", `${integration.providerDisplayName} configuration deleted.`);
      await loadOverview(selectedOrganizationId, token, false);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not delete integration", message);
    } finally {
      setRefreshing(false);
    }
  }

  if (loading) {
    return null;
  }

  return (
    <div className="space-y-6">
      <section className="rounded-3xl border border-border bg-card p-6">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div className="space-y-3">
            <Badge variant="success" className="w-fit">
              <Settings2 className="mr-1 h-3 w-3" /> Settings / Integrations
            </Badge>
            <div>
              <h1 className="text-3xl font-semibold tracking-tight">Organization integrations</h1>
              <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
                Configure {providers.length} third-party providers per organization. Credentials are encrypted and
                resolved via integration abstractions, not appsettings.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-end gap-3">
            {isPlatformAdmin && organizations.length > 0 && (
              <div className="flex flex-col gap-1">
                <span className="text-sm font-medium">
                  Organization
                </span>
                <Select
                  className="w-[220px] cursor-pointer"
                  value={selectedOrganizationId}
                  onChange={(event) => changeSelectedOrganization(event.target.value)}
                >
                  {organizations.map((organization) => (
                    <option key={organization.id} value={organization.id}>
                      {organization.name}
                    </option>
                  ))}
                </Select>
              </div>
            )}

            <Button variant="outline" onClick={() => void refreshOverview()} disabled={!selectedOrganizationId || !token || manualRefreshing}>
              <RefreshCcw className={cn("h-4 w-4", manualRefreshing && "animate-spin")} /> Refresh
            </Button>
          </div>
        </div>
      </section>

      {error ? (
        <div className="flex items-start gap-2 rounded-xl border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          <span className="flex-1">{error}</span>
          <button type="button" onClick={() => setError(null)} className="shrink-0 opacity-70 hover:opacity-100" aria-label="Dismiss error">
            <X className="h-4 w-4" />
          </button>
        </div>
      ) : null}

      {!selectedOrganizationId ? (
        <Card>
          <CardContent className="p-6 text-sm text-muted-foreground">
            Create at least one organization in the Organizations module before configuring integrations.
          </CardContent>
        </Card>
      ) : null}

      {overview ? (
        <>
          <section className="grid gap-4 lg:grid-cols-2">
            {overview.checklistGroups.map((checklist) => (
              <Card key={checklist.key}>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <CheckCircle2 className="h-4 w-4 text-primary" /> {checklist.label}
                  </CardTitle>
                </CardHeader>
                <CardContent className="grid gap-3">
                  {checklist.items.map((item) => (
                    <div key={item.key} className="rounded-xl border border-border bg-background/40 p-4">
                      <div className="flex items-start justify-between gap-3">
                        <p className="font-medium">{item.label}</p>
                        <Badge variant={item.isCompleted ? "success" : "muted"}>{item.isCompleted ? "Done" : "Pending"}</Badge>
                      </div>
                      <p className="mt-2 text-xs text-muted-foreground">{item.requirement}</p>
                    </div>
                  ))}
                </CardContent>
              </Card>
            ))}
          </section>

          {isPlatformAdmin ? (
            <Card>
              <CardHeader className="space-y-3">
                <CardTitle className="flex items-center gap-2 text-base">
                  <Shield className="h-4 w-4 text-primary" /> Workflow routing by organization
                </CardTitle>
                <p className="text-sm text-muted-foreground">
                  System-admin only. Selectors below come from the workflow catalog, active social networks,
                  connected providers, synchronized AI models, and the Apify actor mappings configured for the
                  selected organization. Drag provider routes inside each stage to define the automatic primary
                  and backup execution order.
                </p>
              </CardHeader>
              <CardContent className="space-y-6">
                <div className="grid gap-4 xl:grid-cols-[minmax(0,240px)_minmax(0,240px)_1fr]">
                  <div className="space-y-2">
                    <span className="text-sm font-medium">
                      Workflow
                    </span>
                    <Select
                      className="w-full"
                      value={selectedWorkflowKey}
                      onChange={(event) => setSelectedWorkflowKey(event.target.value)}
                      disabled={workflowOptions.length === 0 || workflowLoading}
                    >
                      {workflowOptions.length === 0 ? (
                        <option value="">Loading workflow catalog...</option>
                      ) : null}
                      {workflowOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </Select>
                  </div>

                  <div className="space-y-2">
                    <span className="text-sm font-medium">
                      Organization
                    </span>
                    <div className="flex h-9 items-center rounded-md border border-border bg-background px-3 text-sm">
                      {organizations.find((organization) => organization.id === selectedOrganizationId)?.name
                        ?? selectedOrganizationId}
                    </div>
                  </div>

                  <div className="flex flex-wrap items-center justify-start gap-2 xl:justify-end xl:self-end">
                    <Button
                      type="button"
                      variant="outline"
                      onClick={seedWorkflowRows}
                      disabled={workflowLoading || !selectedWorkflowDefinition || connectedWorkflowIntegrations.length === 0}
                    >
                      <Plus className="h-4 w-4" /> Seed catalog stages
                    </Button>
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => addWorkflowStage()}
                      disabled={workflowLoading || !selectedWorkflowDefinition || connectedWorkflowIntegrations.length === 0}
                    >
                      <Plus className="h-4 w-4" /> Add stage
                    </Button>
                    <Button
                      type="button"
                      onClick={() => void saveWorkflowConfiguration()}
                      disabled={
                        workflowLoading
                        || workflowSaving
                        || !selectedWorkflowDefinition
                        || connectedWorkflowIntegrations.length === 0
                      }
                    >
                      {workflowSaving ? "Saving..." : "Save workflow"}
                    </Button>
                  </div>
                </div>

                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                  <div className="rounded-xl border border-border bg-background/40 p-4">
                    <p className="text-xs font-medium uppercase tracking-widest text-muted-foreground">Catalog stages</p>
                    <p className="mt-2 text-2xl font-semibold">
                      {selectedWorkflowDefinition?.analysisTypes.reduce((total, analysisType) => total + analysisType.steps.length, 0) ?? 0}
                    </p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      Shared workflow catalog exposed by the API.
                    </p>
                  </div>
                  <div className="rounded-xl border border-border bg-background/40 p-4">
                    <p className="text-xs font-medium uppercase tracking-widest text-muted-foreground">Configured stages</p>
                    <p className="mt-2 text-2xl font-semibold">{workflowStages.length}</p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      Shared step selectors live once per stage instead of repeating on every route.
                    </p>
                  </div>
                  <div className="rounded-xl border border-border bg-background/40 p-4">
                    <p className="text-xs font-medium uppercase tracking-widest text-muted-foreground">Backup routes</p>
                    <p className="mt-2 text-2xl font-semibold">
                      {workflowStages.reduce((total, stage) => total + Math.max(stage.routes.length - 1, 0), 0)}
                    </p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      Every stage can try another provider, model, or actor when the primary route fails.
                    </p>
                  </div>
                  <div className="rounded-xl border border-border bg-background/40 p-4">
                    <p className="text-xs font-medium uppercase tracking-widest text-muted-foreground">Apify actors</p>
                    <p className="mt-2 text-2xl font-semibold">{workflowApifyActors.length}</p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      Loaded from the selected organization&apos;s Apify connector.
                    </p>
                  </div>
                </div>

                {!selectedWorkflowDefinition ? (
                  <div className="rounded-xl border border-border bg-muted/30 p-4 text-sm text-muted-foreground">
                    {selectedWorkflowLabel} has no configurable workflow steps yet. This tool becomes
                    configurable once its workflow catalog is published; the connector checklist above still
                    tracks the providers it will need.
                  </div>
                ) : connectedWorkflowIntegrations.length === 0 ? (
                  <div className="rounded-xl border border-border bg-muted/30 p-4 text-sm text-muted-foreground">
                    Connect at least one provider for this organization before routing workflow steps.
                  </div>
                ) : workflowStages.length === 0 ? (
                  <div className="rounded-xl border border-border bg-muted/30 p-4 text-sm text-muted-foreground">
                    No workflow stages configured yet for {selectedWorkflowDefinition.label}.
                  </div>
                ) : (
                  <div className="space-y-4">
                    {workflowStepGroups.map((group) => {
                      const analysis = analysisTypeDefinition(group.analysisType);
                      const step = stepDefinition(group.analysisType, group.stepKey);
                      const analysisOptions = appendCurrentOption(
                        selectedWorkflowDefinition.analysisTypes.map((item) => ({
                          value: item.key,
                          label: item.label
                        })),
                        group.analysisType,
                        `${group.analysisType} (current stage)`
                      );
                      const stepOptions = appendCurrentOption(
                        (analysis?.steps ?? []).map((item) => ({
                          value: item.key,
                          label: item.label
                        })),
                        group.stepKey,
                        `${group.stepKey} (current stage)`
                      );
                      const requiresNetwork = Boolean(step?.requiresNetwork);
                      const networkTabs = networkTabsForGroup(group, step);
                      const activeNetwork = activeNetworkForGroup(group, step);
                      const activeStage = requiresNetwork
                        ? group.networkStages.find((networkStage) => networkStage.networkKey === activeNetwork) ?? null
                        : group.networkStages[0] ?? null;
                      const activeRoutes = activeStage?.routes ?? [];
                      const activeNetworkLabel = requiresNetwork
                        ? networkTabs.find((tab) => tab.value === activeNetwork)?.label ?? activeNetwork
                        : null;
                      const totalRoutes = group.networkStages.reduce(
                        (total, networkStage) => total + networkStage.routes.length,
                        0
                      );
                      const isCollapsed = collapsedGroups[group.key] ?? true;

                      return (
                        <div key={group.key} className="rounded-2xl border border-border bg-background/40 p-5">
                          <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                            <div className="flex items-start gap-3">
                              <button
                                type="button"
                                onClick={() => setCollapsedGroups((prev) => ({ ...prev, [group.key]: !isCollapsed }))}
                                aria-expanded={!isCollapsed}
                                aria-label={isCollapsed ? "Expand workflow step" : "Collapse workflow step"}
                                className="mt-0.5 rounded-md border border-border bg-card p-2 text-muted-foreground transition-colors hover:text-foreground"
                              >
                                <ChevronDown className={cn("h-4 w-4 transition-transform", isCollapsed && "-rotate-90")} />
                              </button>
                              <div className="space-y-3">
                              <div className="flex flex-wrap items-center gap-2">
                                <Badge variant="outline">{analysis?.label ?? (group.analysisType || "Workflow stage")}</Badge>
                                {step ? (
                                  <Badge variant="muted">{step.operationLabel}</Badge>
                                ) : null}
                                {activeNetworkLabel ? (
                                  <Badge variant="info">{activeNetworkLabel}</Badge>
                                ) : null}
                                <Badge variant={totalRoutes > 0 ? "success" : "muted"}>
                                  {totalRoutes} {totalRoutes === 1 ? "route" : "routes"}
                                </Badge>
                              </div>
                              <div>
                                <p className="font-medium">{step?.label ?? "Select a workflow stage"}</p>
                                <p className="mt-1 text-sm text-muted-foreground">
                                  {step?.description ?? "Choose the analysis type and step to load the correct selectors."}
                                </p>
                              </div>
                              </div>
                            </div>

                            {!isCollapsed ? (
                            <div className="flex flex-wrap items-center gap-2">
                              <Button
                                type="button"
                                variant="outline"
                                size="sm"
                                onClick={() => addWorkflowRouteForNetwork(group.key, activeNetwork)}
                                disabled={requiresNetwork && !activeNetwork}
                              >
                                <Plus className="h-4 w-4" /> {activeRoutes.length === 0 ? "Add route" : "Add backup route"}
                              </Button>
                              <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                onClick={() => removeWorkflowNetworkStage(group.key, activeNetwork)}
                                disabled={activeRoutes.length === 0}
                              >
                                <Trash2 className="h-4 w-4" />{" "}
                                {requiresNetwork && activeNetworkLabel ? `Remove ${activeNetworkLabel} routes` : "Remove stage"}
                              </Button>
                            </div>
                            ) : null}
                          </div>

                          {!isCollapsed ? (
                          <>
                          <div className="mt-5 grid gap-4 md:grid-cols-2">
                            <div className="space-y-2">
                              <span className="text-sm font-medium">
                                Analysis type
                              </span>
                              <Select
                                className="w-full"
                                value={group.analysisType}
                                onChange={(event) => patchWorkflowStepGroup(group.key, { analysisType: event.target.value })}
                              >
                                <option value="" disabled>
                                  Select analysis type
                                </option>
                                {analysisOptions.map((option) => (
                                  <option key={option.value} value={option.value}>
                                    {option.label}
                                  </option>
                                ))}
                              </Select>
                            </div>

                            <div className="space-y-2">
                              <span className="text-sm font-medium">
                                Stage
                              </span>
                              <Select
                                className="w-full"
                                value={group.stepKey}
                                onChange={(event) => patchWorkflowStepGroup(group.key, { stepKey: event.target.value })}
                              >
                                <option value="" disabled>
                                  Select workflow stage
                                </option>
                                {stepOptions.map((option) => (
                                  <option key={option.value} value={option.value}>
                                    {option.label}
                                  </option>
                                ))}
                              </Select>
                            </div>
                          </div>

                          {requiresNetwork ? (
                            <div className="mt-5 space-y-2">
                              <span className="text-sm font-medium">
                                Network
                              </span>
                              {networkTabs.length === 0 ? (
                                <div className="rounded-md border border-border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
                                  No active social networks are available for this step.
                                </div>
                              ) : (
                                <div className="flex flex-wrap items-center gap-2">
                                  {networkTabs.map((tab) => {
                                    const tabStage = group.networkStages.find(
                                      (networkStage) => networkStage.networkKey === tab.value
                                    );
                                    const tabRoutes = tabStage?.routes.length ?? 0;
                                    const isActive = tab.value === activeNetwork;
                                    return (
                                      <button
                                        key={tab.value}
                                        type="button"
                                        onClick={() => setActiveNetworkByGroup((prev) => ({ ...prev, [group.key]: tab.value }))}
                                        aria-pressed={isActive}
                                        className={cn(
                                          "inline-flex items-center gap-2 rounded-full border px-3 py-1 text-sm transition-colors",
                                          isActive
                                            ? "border-primary bg-primary/10 text-foreground"
                                            : "border-border text-muted-foreground hover:text-foreground"
                                        )}
                                      >
                                        {tab.label}
                                        <Badge variant={tabRoutes > 0 ? "success" : "muted"}>{tabRoutes}</Badge>
                                      </button>
                                    );
                                  })}
                                </div>
                              )}
                            </div>
                          ) : null}

                          <div className="mt-5 rounded-2xl border border-dashed border-border/70 bg-card/60 p-4">
                            <div className="flex flex-wrap items-center justify-between gap-3">
                              <div>
                                <p className="text-sm font-medium">
                                  Execution routes{activeNetworkLabel ? ` — ${activeNetworkLabel}` : ""}
                                </p>
                                <p className="mt-1 text-xs text-muted-foreground">
                                  Drag the providers below to define the automatic order. The first route runs first;
                                  every next one becomes a backup fallback for this network.
                                </p>
                              </div>
                              <Badge variant="outline">
                                {activeRoutes.length === 0
                                  ? "No routes yet"
                                  : activeRoutes.length === 1
                                    ? "No backups yet"
                                    : `${activeRoutes.length - 1} backup ${activeRoutes.length - 1 === 1 ? "route" : "routes"}`}
                              </Badge>
                            </div>

                            {!activeStage || activeRoutes.length === 0 ? (
                              <div className="mt-4 rounded-xl border border-border bg-muted/20 p-4 text-sm text-muted-foreground">
                                {requiresNetwork && activeNetworkLabel
                                  ? `No execution routes for ${activeNetworkLabel} yet. Add the primary route to start.`
                                  : "No execution routes yet. Add the primary route to start."}
                              </div>
                            ) : (
                              <DndContext
                                sensors={workflowRouteSensors}
                                collisionDetection={closestCenter}
                                onDragEnd={(event) => handleWorkflowRouteDragEnd(activeStage.key, activeStage, event)}
                              >
                                <SortableContext
                                  items={activeRoutes.map((route) => route.id)}
                                  strategy={verticalListSortingStrategy}
                                >
                                  <div className="mt-4 space-y-3">
                                    {activeRoutes.map((route) => {
                                      const providerOptions = appendCurrentOption(
                                        providerOptionsForStep(step).map((integration) => ({
                                          value: integration.providerKey,
                                          label: integration.providerDisplayName
                                        })),
                                        route.providerKey,
                                        `${providerDisplayName(route.providerKey)} (current route)`
                                      );
                                      const modelOptions = modelOptionsForProvider(route.providerKey, route.modelId, step);
                                      const actorOptions = actorOptionsForStep(step, activeNetwork, route.actorId);
                                      const routeTitle = [
                                        route.providerKey ? providerDisplayName(route.providerKey) : "",
                                        step?.supportsModelSelection ? route.modelId : "",
                                        step?.supportsActorSelection ? route.actorId : ""
                                      ]
                                        .filter((value) => value && value.trim().length > 0)
                                        .join(" · ");

                                      return (
                                        <WorkflowSortableRouteCard
                                          key={route.id}
                                          id={route.id}
                                          routeLabel={routeDisplayLabel(route.priority)}
                                          order={route.priority}
                                          title={routeTitle || "Select a connected provider"}
                                          subtitle={route.priority === 1
                                            ? "Runs first for this network."
                                            : "Runs only if a previous route fails or is unavailable."}
                                          isEnabled={route.isEnabled}
                                          onEnabledChange={(checked) => patchWorkflowRow(route.id, { isEnabled: checked })}
                                          onRemove={() => removeWorkflowRow(route.id)}
                                        >
                                          <div className="space-y-2">
                                            <span className="text-sm font-medium">
                                              Provider / connector
                                            </span>
                                            <Select
                                              className="w-full"
                                              value={route.providerKey}
                                              onChange={(event) => patchWorkflowRow(route.id, { providerKey: event.target.value })}
                                            >
                                              <option value="" disabled>
                                                Select provider
                                              </option>
                                              {providerOptions.map((option) => (
                                                <option key={option.value} value={option.value}>
                                                  {option.label}
                                                </option>
                                              ))}
                                            </Select>
                                          </div>

                                          {step?.supportsModelSelection ? (
                                            <div className="space-y-2">
                                              <span className="text-sm font-medium">
                                                Model
                                              </span>
                                              <Select
                                                className="w-full"
                                                value={route.modelId}
                                                onChange={(event) => patchWorkflowRow(route.id, { modelId: event.target.value })}
                                                disabled={modelOptions.length === 0}
                                              >
                                                <option value="" disabled>
                                                  {modelOptions.length === 0
                                                    ? "Sync models in this provider first"
                                                    : "Select synchronized model"}
                                                </option>
                                                {modelOptions.map((option) => (
                                                  <option key={option.value} value={option.value}>
                                                    {option.label}
                                                  </option>
                                                ))}
                                              </Select>
                                              <p className="text-xs text-muted-foreground">
                                                Pulled from the selected organization&apos;s AI provider connector.
                                              </p>
                                            </div>
                                          ) : null}

                                          {step?.supportsActorSelection ? (
                                            <div className="space-y-2 lg:col-span-2">
                                              <span className="text-sm font-medium">
                                                Actor / connector route
                                              </span>
                                              {route.providerKey === "apify" ? (
                                                <>
                                                  <Select
                                                    className="w-full"
                                                    value={route.actorId}
                                                    onChange={(event) => patchWorkflowRow(route.id, { actorId: event.target.value })}
                                                    disabled={!activeNetwork || actorOptions.length === 0}
                                                  >
                                                    <option value="" disabled>
                                                      {actorOptions.length === 0
                                                        ? "Configure actor mappings in the Apify connector"
                                                        : "Select configured actor"}
                                                    </option>
                                                    {actorOptions.map((option) => (
                                                      <option key={option.value} value={option.value}>
                                                        {option.label}
                                                      </option>
                                                    ))}
                                                  </Select>
                                                  <p className="text-xs text-muted-foreground">
                                                    Pulled from this organization&apos;s Apify connector for the selected network
                                                    and workflow area.
                                                  </p>
                                                </>
                                              ) : (
                                                <div className="rounded-md border border-border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
                                                  Actor routing is available only for Apify on this stage.
                                                </div>
                                              )}
                                            </div>
                                          ) : null}
                                        </WorkflowSortableRouteCard>
                                      );
                                    })}
                                  </div>
                                </SortableContext>
                              </DndContext>
                            )}
                          </div>
                          </>
                          ) : null}
                        </div>
                      );
                    })}
                  </div>
                )}
              </CardContent>
            </Card>
          ) : null}

          {groupedIntegrations.map(([category, integrations]) => (
            <section key={category} className="space-y-3">
              <div className="flex items-center gap-2">
                <Sparkles className="h-4 w-4 text-primary" />
                <h2 className="text-lg font-semibold">{categoryLabels[category] ?? category}</h2>
              </div>

              <div className="grid gap-4 lg:grid-cols-2">
                {integrations.map((integration) => {
                  const ProviderIcon = providerIcons[integration.iconKey] ?? Sparkles;

                  return (
                    <Card key={integration.providerKey}>
                      <CardHeader>
                        <CardTitle className="flex flex-wrap items-center gap-2 text-base">
                          <span className="rounded-xl border border-border bg-muted p-2">
                            <ProviderIcon className="h-4 w-4 text-primary" aria-hidden="true" />
                          </span>
                          <span>{integration.providerDisplayName}</span>
                          <StatusBadge status={integration.status} />
                          {integration.isReadOnly ? (
                            <Badge variant="muted" className="gap-1">
                              <Shield className="h-3 w-3" /> System default read-only
                            </Badge>
                          ) : null}
                        </CardTitle>
                      </CardHeader>
                    <CardContent className="space-y-4">
                      <div className="grid gap-2 text-sm text-muted-foreground">
                        <p>
                          Mode: <span className="text-foreground">{integration.connectionMode}</span>
                        </p>
                        {integration.selectedAiModel ? (
                          <p>
                            Selected model: <span className="text-foreground">{integration.selectedAiModel}</span>
                          </p>
                        ) : null}
                        {integration.lastError ? (
                          <p className="text-destructive/80 text-sm">Last error: {integration.lastError}</p>
                        ) : null}
                      </div>

                      <div className="flex flex-wrap gap-2">
                        <Button variant="secondary" size="sm" onClick={() => void openConfigure(integration)}>
                          <KeyRound className="h-4 w-4" /> Connect / Configure
                        </Button>
                        {/* OAuth-connect providers (e.g. Canva): one-time org account connection.
                            Shown only once the connector app credentials (client_id/secret) exist. */}
                        {OAUTH_CONNECT_PROVIDERS.has(integration.providerKey) && integration.integrationId ? (
                          <Button variant="outline" size="sm" onClick={() => void startOAuthConnect(integration)}>
                            <Link2 className="h-4 w-4" /> Connect account
                          </Button>
                        ) : null}
                        <Button variant="outline" size="sm" onClick={() => void runTest(integration)}>
                          <Link2 className="h-4 w-4" /> Test connection
                        </Button>
                        {integration.category === "AiModel" && integration.status === "Connected" ? (
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => void runModelSync(integration)}
                            disabled={syncingModelsProviderKey === integration.providerKey}
                          >
                            <RefreshCcw
                              className={cn(
                                "h-4 w-4",
                                syncingModelsProviderKey === integration.providerKey && "animate-spin"
                              )}
                            />
                            Sync models
                          </Button>
                        ) : null}
                        {integration.integrationId ? (
                          <Button variant="destructive" size="sm" onClick={() => setDeleteConfirmIntegration(integration)}>
                            <Unplug className="h-4 w-4" /> Delete config
                          </Button>
                        ) : null}
                      </div>
                    </CardContent>
                    </Card>
                  );
                })}
              </div>
            </section>
          ))}
        </>
      ) : null}

      <Dialog open={modal !== null} onOpenChange={(open) => !open && setModal(null)}>
        <DialogContent className="sm:max-w-2xl max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>{modal?.integration.providerDisplayName} integration</DialogTitle>
            <DialogDescription>
              Configure connection mode, credentials, and optional AI model per organization.
            </DialogDescription>
          </DialogHeader>

          {modal ? (
            <form className="space-y-4" onSubmit={saveConfiguration}>
              <div className="space-y-2">
                <label className="text-sm font-medium">Connection mode</label>
                <Select
                  className="w-full"
                  value={modal.mode}
                  onChange={(event) => setModal((prev) => (prev ? { ...prev, mode: event.target.value as IntegrationConnectionMode } : prev))}
                >
                  {modal.integration.supportsBringYourOwnKey ? (
                    <option value="BringYourOwnKey">Bring your own key</option>
                  ) : null}
                  {modal.integration.supportsManagedByIncoders ? (
                    <option value="ManagedByIncoders">Managed by Incoders</option>
                  ) : null}
                  {modal.integration.supportsSystemDefault ? (
                    <option value="SystemDefault">System default (host)</option>
                  ) : null}
                </Select>
              </div>

              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={modal.enabled}
                  onChange={(event) => setModal((prev) => (prev ? { ...prev, enabled: event.target.checked } : prev))}
                />
                Enabled
              </label>

              {modal.integration.category === "AiModel" ? (
                <div className="space-y-2">
                  <label className="text-sm font-medium">Default model (optional)</label>
                  <ModelSelect
                    models={modal.integration.availableModels}
                    value={modal.selectedAiModel}
                    onChange={(v) => setModal((prev) => (prev ? { ...prev, selectedAiModel: v } : prev))}
                    placeholder="e.g. gpt-4.1-mini"
                  />
                  <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
                    <span>
                      {modal.integration.availableModels.length > 0
                        ? `${modal.integration.availableModels.length} organization model(s) available.`
                        : "No synchronized models yet; manual entry is still allowed as a fallback."}
                    </span>
                    {modal.integration.status === "Connected" ? (
                      <Button
                        type="button"
                        size="sm"
                        variant="outline"
                        onClick={() => void runModelSync(modal.integration)}
                        disabled={syncingModelsProviderKey === modal.integration.providerKey}
                      >
                        <RefreshCcw
                          className={cn(
                            "h-4 w-4",
                            syncingModelsProviderKey === modal.integration.providerKey && "animate-spin"
                          )}
                        />
                        Sync models
                      </Button>
                    ) : null}
                  </div>
                </div>
              ) : null}

              {modal.mode === "SystemDefault" && modal.integration.systemDefaultReadOnly ? (
                <div className="rounded-xl border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
                  This provider has a readonly system default mode. Switch to BYOK/Managed to override with
                  organization credentials.
                </div>
              ) : null}

              {modal.mode !== "SystemDefault"
                ? modal.integration.credentialFields
                  .filter((field) => field.key !== apifyActorMappingsCredentialKey && field.key !== "actor_id")
                  .map((field) => {
                    const isConfigured = modal.integration.configuredCredentialKeys.includes(field.key);
                    return (
                      <div key={field.key} className="space-y-2">
                        <label className="text-sm font-medium">
                          {field.label} {field.isRequired ? <span className="text-red-300">*</span> : null}
                        </label>
                        {field.isSecret ? (
                          <PasswordInput
                            value={modal.credentials[field.key] ?? ""}
                            onChange={(event) =>
                              setModal((prev) =>
                                prev
                                  ? {
                                      ...prev,
                                      credentials: {
                                        ...prev.credentials,
                                        [field.key]: event.target.value
                                      }
                                    }
                                  : prev
                              )
                            }
                            placeholder={field.placeholder ?? undefined}
                          />
                        ) : (
                          <Input
                            type="text"
                            value={modal.credentials[field.key] ?? ""}
                            onChange={(event) =>
                              setModal((prev) =>
                                prev
                                  ? {
                                      ...prev,
                                      credentials: {
                                        ...prev.credentials,
                                        [field.key]: event.target.value
                                      }
                                    }
                                  : prev
                              )
                            }
                            placeholder={field.placeholder ?? undefined}
                          />
                        )}
                        <p className="text-xs text-muted-foreground">
                          {field.helpText ?? ""} {isConfigured ? "(Existing value already configured)" : ""}
                        </p>
                      </div>
                    );
                  })
                : null}

              {modal.integration.providerKey === "apify" && modal.mode !== "SystemDefault" ? (
                <div className="space-y-3 rounded-xl border border-border bg-background/40 p-3">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="text-sm font-medium">Actor routing</p>
                      <p className="text-xs text-muted-foreground">
                        Map Apify actors by active social network.
                      </p>
                    </div>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={addApifyActorMapping}
                      disabled={socialNetworkOptions.length === 0}
                    >
                      <Plus className="h-4 w-4" /> Add actor
                    </Button>
                  </div>

                  {socialNetworkOptions.length === 0 ? (
                    <p className="text-xs text-muted-foreground">
                      No active social networks are available in the platform catalog.
                    </p>
                  ) : modal.apifyActorMappings.length === 0 ? (
                    <p className="text-xs text-muted-foreground">
                      No actor routes configured yet.
                    </p>
                  ) : (
                    <div className="space-y-2">
                      <div className="hidden md:grid gap-2 px-2 md:grid-cols-[1fr_1fr_1fr_auto_auto]">
                        <span className="text-xs font-medium text-muted-foreground">Red social</span>
                        <span className="text-xs font-medium text-muted-foreground">Workflow / Área</span>
                        <span className="text-xs font-medium text-muted-foreground">Actor ID</span>
                        <span className="text-xs font-medium text-muted-foreground">Principal</span>
                        <span />
                      </div>
                      {modal.apifyActorMappings.map((mapping) => {
                        const isKnownNetwork = socialNetworkOptions.some((option) => option.value === mapping.networkKey);

                        return (
                          <div key={mapping.id} className="grid gap-2 rounded-lg border border-border/70 p-2 md:grid-cols-[1fr_1fr_1fr_auto_auto]">
                            <Select
                              value={mapping.networkKey}
                              onChange={(event) =>
                                patchApifyActorMapping(mapping.id, {
                                  networkKey: event.target.value
                                })
                              }
                            >
                              {!isKnownNetwork ? (
                                <option value={mapping.networkKey}>{mapping.networkKey} (inactive)</option>
                              ) : null}
                              {socialNetworkOptions.map((option) => (
                                <option key={option.value} value={option.value}>
                                  {option.label}
                                </option>
                              ))}
                            </Select>
                            <Select
                              value={mapping.variantKey}
                              onChange={(event) =>
                                patchApifyActorMapping(mapping.id, {
                                  variantKey: event.target.value
                                })
                              }
                            >
                              {APIFY_VARIANT_OPTIONS.map((option) => (
                                <option key={option.value} value={option.value}>
                                  {option.label}
                                </option>
                              ))}
                            </Select>
                            <Input
                              value={mapping.actorId}
                              onChange={(event) =>
                                patchApifyActorMapping(mapping.id, { actorId: event.target.value })
                              }
                              placeholder="Apify actor ID"
                            />
                            <label
                              className="flex items-center gap-1 text-xs text-muted-foreground cursor-pointer select-none whitespace-nowrap"
                              title="Marcar como actor principal para esta red y área"
                            >
                              <input
                                type="checkbox"
                                checked={mapping.isPrimary}
                                onChange={() => setPrimaryActorMapping(mapping.id)}
                                className="accent-primary"
                              />
                              Principal
                            </label>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => removeApifyActorMapping(mapping.id)}
                              aria-label="Remove actor route"
                            >
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              ) : null}

              <div className="flex justify-end gap-2">
                {modal.integration.integrationId ? (
                  <Button
                    type="button"
                    variant="destructive"
                    onClick={() => {
                      setModal(null);
                      setDeleteConfirmIntegration(modal.integration);
                    }}
                  >
                    <Trash2 className="h-4 w-4" /> Delete
                  </Button>
                ) : null}
                <Button type="button" variant="secondary" onClick={() => setModal(null)}>
                  Cancel
                </Button>
                <Button type="submit" disabled={saving}>
                  {saving ? "Saving..." : "Save"}
                </Button>
              </div>
            </form>
          ) : null}
        </DialogContent>
      </Dialog>

      <Dialog open={deleteConfirmIntegration !== null} onOpenChange={(open) => !open && setDeleteConfirmIntegration(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete configuration</DialogTitle>
            <DialogDescription>
              Are you sure you want to delete the <strong>{deleteConfirmIntegration?.providerDisplayName}</strong> configuration?
              Any checklist steps that depend on this integration will revert to Pending.
            </DialogDescription>
          </DialogHeader>
          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="secondary" onClick={() => setDeleteConfirmIntegration(null)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={() => {
                if (deleteConfirmIntegration) {
                  void removeConfiguration(deleteConfirmIntegration);
                }
                setDeleteConfirmIntegration(null);
              }}
            >
              <Trash2 className="h-4 w-4" /> Delete
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function WorkflowSortableRouteCard({
  id,
  routeLabel,
  order,
  title,
  subtitle,
  isEnabled,
  onEnabledChange,
  onRemove,
  children
}: {
  id: string;
  routeLabel: string;
  order: number;
  title: string;
  subtitle: string;
  isEnabled: boolean;
  onEnabledChange: (checked: boolean) => void;
  onRemove: () => void;
  children: ReactNode;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={cn(
        "rounded-2xl border border-border bg-background p-4",
        isDragging && "z-10 border-primary/50 shadow-lg"
      )}
    >
      <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
        <div className="flex items-start gap-3">
          <button
            type="button"
            className="mt-0.5 cursor-grab rounded-md border border-border bg-card p-2 text-muted-foreground hover:text-foreground active:cursor-grabbing"
            aria-label="Drag to reorder workflow route"
            {...attributes}
            {...listeners}
          >
            <GripVertical className="h-4 w-4" />
          </button>

          <div className="space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={order === 1 ? "success" : "outline"}>{routeLabel}</Badge>
              <Badge variant="muted">Order {order}</Badge>
            </div>
            <div>
              <p className="font-medium">{title}</p>
              <p className="mt-1 text-sm text-muted-foreground">{subtitle}</p>
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <label className="flex min-h-9 items-center gap-2 rounded-md border border-border bg-card px-3 text-sm">
            <Checkbox checked={isEnabled} onChange={(event) => onEnabledChange(event.target.checked)} />
            Enabled
          </label>
          <Button type="button" variant="ghost" size="sm" onClick={onRemove} aria-label="Remove workflow route">
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      </div>

      <div className="mt-4 grid gap-4 lg:grid-cols-2">
        {children}
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: IntegrationStatus }) {
  if (status === "Connected") {
    return <Badge variant="success">Connected on</Badge>;
  }

  if (status === "Configured") {
    return <Badge variant="outline">Configured</Badge>;
  }

  if (status === "Failed") {
    return <Badge variant="destructive">Failed</Badge>;
  }

  if (status === "Disabled") {
    return <Badge variant="muted">Connected off</Badge>;
  }

  return <Badge variant="muted">Not configured</Badge>;
}
