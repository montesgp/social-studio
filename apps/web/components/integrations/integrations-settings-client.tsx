"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
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
  RefreshCcw,
  Settings2,
  Shield,
  Sparkles,
  Trash2,
  Unplug,
  WandSparkles,
  Youtube,
  Instagram
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import {
  configureOrganizationIntegration,
  deleteOrganizationIntegration,
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
  OrganizationIntegrationsOverview
} from "@/lib/api/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { useToast } from "@/components/ui/toaster";
import { cn } from "@/lib/utils";
import { useAccessToken } from "@/components/auth/use-access-token";

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
}

interface IntegrationsSettingsClientProps {
  isPlatformAdmin: boolean;
}

export function IntegrationsSettingsClient({ isPlatformAdmin }: IntegrationsSettingsClientProps) {
  const token = useAccessToken();
  const toast = useToast();

  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [providers, setProviders] = useState<IntegrationProvider[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState<string>("");
  const [overview, setOverview] = useState<OrganizationIntegrationsOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [defaultAiProviderKey, setDefaultAiProviderKey] = useState<string>("");
  const [defaultAiModel, setDefaultAiModel] = useState<string>("");
  const [modal, setModal] = useState<ModalState | null>(null);

  useEffect(() => {
    if (!token) {
      setLoading(false);
      setOverview(null);
      setOrganizations([]);
      setProviders([]);
      return;
    }

    void bootstrap(token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  useEffect(() => {
    if (!token || !selectedOrganizationId) {
      return;
    }

    void loadOverview(selectedOrganizationId, token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedOrganizationId]);

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

  async function bootstrap(accessToken: string) {
    setLoading(true);
    setError(null);

    try {
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

    if (useRefreshState) {
      setRefreshing(true);
    }

    setError(null);

    try {
      const data = await listOrganizationIntegrations(organizationId, accessToken);
      setOverview(data);
      setDefaultAiProviderKey(data.defaultAiProviderKey ?? "");
      setDefaultAiModel(data.defaultAiModel ?? "");
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      if (useRefreshState) {
        setRefreshing(false);
      }
    }
  }

  function openConfigure(integration: OrganizationIntegration) {
    const mode = integration.isReadOnly
      ? integration.supportsBringYourOwnKey
        ? "BringYourOwnKey"
        : integration.supportsManagedByIncoders
          ? "ManagedByIncoders"
          : integration.connectionMode
      : integration.connectionMode;

    setModal({
      integration,
      mode,
      enabled: integration.status !== "Disabled",
      selectedAiModel: integration.selectedAiModel ?? "",
      credentials: {}
    });
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
        .filter(([, value]) => value.trim().length > 0)
        .map(([key, value]) => ({ key, value }));

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
      setError(message);
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
                <select
                  className="h-9 w-[220px] cursor-pointer rounded-md border border-border bg-background px-3 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-primary/50"
                  value={selectedOrganizationId}
                  onChange={(event) => setSelectedOrganizationId(event.target.value)}
                >
                  {organizations.map((organization) => (
                    <option key={organization.id} value={organization.id}>
                      {organization.name}
                    </option>
                  ))}
                </select>
              </div>
            )}

            <Button variant="outline" onClick={() => selectedOrganizationId && token && loadOverview(selectedOrganizationId, token)}>
              <RefreshCcw className={cn("h-4 w-4", refreshing && "animate-spin")} /> Refresh
            </Button>
          </div>
        </div>
      </section>

      {error ? (
        <div className="rounded-xl border border-red-500/40 bg-red-500/10 px-4 py-3 text-sm text-red-100">{error}</div>
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
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <CheckCircle2 className="h-4 w-4 text-primary" /> Required setup checklist
              </CardTitle>
            </CardHeader>
            <CardContent className="grid gap-3 md:grid-cols-3">
              {overview.checklist.map((item) => (
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

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <Bot className="h-4 w-4 text-primary" /> Default AI provider / model
              </CardTitle>
            </CardHeader>
            <CardContent>
              <form className="grid gap-3 lg:grid-cols-[1fr_1fr_auto]" onSubmit={saveDefaultAi}>
                <select
                  className="h-10 rounded-md border border-border bg-background px-3 text-sm"
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
                </select>
                <Input
                  value={defaultAiModel}
                  onChange={(event) => setDefaultAiModel(event.target.value)}
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
                          <p className="text-red-300">Last error: {integration.lastError}</p>
                        ) : null}
                      </div>

                      <div className="flex flex-wrap gap-2">
                        <Button variant="secondary" size="sm" onClick={() => openConfigure(integration)}>
                          <KeyRound className="h-4 w-4" /> Connect / Configure
                        </Button>
                        <Button variant="outline" size="sm" onClick={() => void runTest(integration)}>
                          <Link2 className="h-4 w-4" /> Test connection
                        </Button>
                        {integration.integrationId ? (
                          <Button variant="destructive" size="sm" onClick={() => void removeConfiguration(integration)}>
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
        <DialogContent>
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
                <select
                  className="h-10 w-full rounded-md border border-border bg-background px-3 text-sm"
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
                </select>
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
                  <Input
                    value={modal.selectedAiModel}
                    onChange={(event) =>
                      setModal((prev) => (prev ? { ...prev, selectedAiModel: event.target.value } : prev))
                    }
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
                ? modal.integration.credentialFields.map((field) => {
                    const isConfigured = modal.integration.configuredCredentialKeys.includes(field.key);
                    return (
                      <div key={field.key} className="space-y-2">
                        <label className="text-sm font-medium">
                          {field.label} {field.isRequired ? <span className="text-red-300">*</span> : null}
                        </label>
                        <Input
                          type={field.isSecret ? "password" : "text"}
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
                        <p className="text-xs text-muted-foreground">
                          {field.helpText ?? ""} {isConfigured ? "(Existing value already configured)" : ""}
                        </p>
                      </div>
                    );
                  })
                : null}

              <div className="flex justify-end gap-2">
                {modal.integration.integrationId ? (
                  <Button
                    type="button"
                    variant="destructive"
                    onClick={() => {
                      void removeConfiguration(modal.integration);
                      setModal(null);
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
    </div>
  );
}

function StatusBadge({ status }: { status: IntegrationStatus }) {
  if (status === "Connected") {
    return <Badge variant="success">Connected</Badge>;
  }

  if (status === "Failed") {
    return <Badge className="bg-red-500/20 text-red-300">Failed</Badge>;
  }

  if (status === "Disabled") {
    return <Badge variant="muted">Disabled</Badge>;
  }

  return <Badge variant="muted">Not configured</Badge>;
}
