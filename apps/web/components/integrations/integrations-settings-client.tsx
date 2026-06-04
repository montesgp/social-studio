"use client";

import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import {
  Bot,
  BrainCircuit,
  CheckCircle2,
  Cloud,
  DatabaseZap,
  FolderOpen,
  KeyRound,
  Link2,
  Loader2,
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
  getOrganizationIntegration,
  listIntegrationProviders,
  listOrganizationIntegrations,
  setDefaultAiIntegration,
  testOrganizationIntegration
} from "@/lib/api/integrations";
import { listOrganizations } from "@/lib/api/organizations";
import {
  ApiError,
  IntegrationConnectionMode,
  IntegrationProvider,
  IntegrationStatus,
  Organization,
  OrganizationIntegration,
  OrganizationIntegrationsOverview,
  SocialNetwork
} from "@/lib/api/types";
import { listSocialNetworks } from "@/lib/api/social-networks";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/ui/password-input";
import { useToast } from "@/components/ui/toaster";
import { cn } from "@/lib/utils";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";

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

const apifyActorMappingsCredentialKey = "actor_mappings_json";

const WORKFLOW_OPTIONS: { value: string; label: string }[] = [
  { value: "social-extraction", label: "Análisis de contenido" },
  { value: "profile", label: "Análisis de perfil" }
];

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

export function IntegrationsSettingsClient({ isPlatformAdmin }: IntegrationsSettingsClientProps) {
  const token = useAccessToken();
  const toast = useToast();
  const { orgId: activeOrgId, isInitialized: orgInitialized } = useActiveOrg();
  const overviewRequestIdRef = useRef(0);

  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [providers, setProviders] = useState<IntegrationProvider[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState<string>("");
  const [overview, setOverview] = useState<OrganizationIntegrationsOverview | null>(null);
  const [socialNetworks, setSocialNetworks] = useState<SocialNetwork[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [manualRefreshing, setManualRefreshing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [defaultAiProviderKey, setDefaultAiProviderKey] = useState<string>("");
  const [defaultAiModel, setDefaultAiModel] = useState<string>("");
  const [modal, setModal] = useState<ModalState | null>(null);
  const [deleteConfirmIntegration, setDeleteConfirmIntegration] = useState<OrganizationIntegration | null>(null);

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
    if (!token || !selectedOrganizationId) {
      return;
    }

    void loadOverview(selectedOrganizationId, token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedOrganizationId]);

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

  const connectedAiIntegrations = useMemo(() => {
    return (overview?.integrations ?? []).filter(
      (integration) => integration.category === "AiModel" && integration.status === "Connected"
    );
  }, [overview?.integrations]);

  const socialNetworkOptions = useMemo(() => {
    return socialNetworks
      .filter((network) => network.isActive)
      .map((network) => ({
        value: network.code,
        label: network.displayName
      }));
  }, [socialNetworks]);

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
        } else {
          setOverview(null);
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
      setDefaultAiProviderKey(data.defaultAiProviderKey ?? "");
      setDefaultAiModel(data.defaultAiModel ?? "");
    } catch (err) {
      if (requestId !== overviewRequestIdRef.current) {
        return;
      }

      setError((err as ApiError).message);
    } finally {
      if (useRefreshState && requestId === overviewRequestIdRef.current) {
        setRefreshing(false);
      }
    }
  }

  function changeSelectedOrganization(organizationId: string) {
    overviewRequestIdRef.current += 1;
    setSelectedOrganizationId(organizationId);
    setOverview(null);
    setDefaultAiProviderKey("");
    setDefaultAiModel("");
    setModal(null);
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

  async function saveDefaultAi(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedOrganizationId || !token || !defaultAiProviderKey) {
      return;
    }

    setRefreshing(true);
    setError(null);

    try {
      await setDefaultAiIntegration(
        selectedOrganizationId,
        {
          providerKey: defaultAiProviderKey,
          model: defaultAiModel || undefined
        },
        token
      );

      toast.success("Default AI updated", "Organization default AI provider/model saved.");
      await loadOverview(selectedOrganizationId, token, false);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not set default AI", message);
    } finally {
      setRefreshing(false);
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
                <span className="text-xs font-medium uppercase tracking-widest text-muted-foreground">
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

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <Bot className="h-4 w-4 text-primary" /> Default AI provider / model
              </CardTitle>
            </CardHeader>
            <CardContent>
              <form className="grid gap-3 lg:grid-cols-[1fr_1fr_auto]" onSubmit={saveDefaultAi}>
                <Select
                  value={defaultAiProviderKey}
                  onChange={(event) => setDefaultAiProviderKey(event.target.value)}
                  required
                >
                  <option value="" disabled>
                    Select connected AI provider
                  </option>
                  {connectedAiIntegrations.map((integration) => (
                    <option key={integration.providerKey} value={integration.providerKey}>
                      {integration.providerDisplayName}
                    </option>
                  ))}
                </Select>
                <ModelSelect
                  models={providers.find((p) => p.key === defaultAiProviderKey)?.availableModels ?? []}
                  value={defaultAiModel}
                  onChange={(v) => setDefaultAiModel(v)}
                  placeholder="Optional model (e.g. gpt-4.1-mini)"
                />
                <Button type="submit" disabled={!defaultAiProviderKey || refreshing}>
                  Save default
                </Button>
              </form>
            </CardContent>
          </Card>

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
                        {integration.isDefaultAi ? (
                          <Badge variant="success" className="w-fit">
                            <Bot className="mr-1 h-3 w-3" /> Default AI provider
                          </Badge>
                        ) : null}
                        {integration.lastError ? (
                          <p className="text-destructive/80 text-sm">Last error: {integration.lastError}</p>
                        ) : null}
                      </div>

                      <div className="flex flex-wrap gap-2">
                        <Button variant="secondary" size="sm" onClick={() => void openConfigure(integration)}>
                          <KeyRound className="h-4 w-4" /> Connect / Configure
                        </Button>
                        <Button variant="outline" size="sm" onClick={() => void runTest(integration)}>
                          <Link2 className="h-4 w-4" /> Test connection
                        </Button>
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
        <DialogContent className="sm:max-w-2xl">
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
                    <option value="BringYourOwnKey">BringYourOwnKey</option>
                  ) : null}
                  {modal.integration.supportsManagedByIncoders ? (
                    <option value="ManagedByIncoders">ManagedByIncoders</option>
                  ) : null}
                  {modal.integration.supportsSystemDefault ? (
                    <option value="SystemDefault">SystemDefault</option>
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
                    models={providers.find((p) => p.key === modal.integration.providerKey)?.availableModels ?? []}
                    value={modal.selectedAiModel}
                    onChange={(v) => setModal((prev) => (prev ? { ...prev, selectedAiModel: v } : prev))}
                    placeholder="e.g. gpt-4.1-mini"
                  />
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
                              {WORKFLOW_OPTIONS.map((option) => (
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
