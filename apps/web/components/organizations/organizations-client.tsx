"use client";

import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { motion } from "framer-motion";
import {
  Archive,
  BrainCircuit,
  Building2,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock3,
  Edit3,
  Eye,
  KeyRound,
  LayoutGrid,
  List,
  Loader2,
  Plus,
  RefreshCcw,
  RotateCcw,
  Search,
  ShieldCheck,
  Sparkles,
  Trash2,
  X,
  Zap
} from "lucide-react";
import {
  activateOrganization,
  createOrganization,
  deactivateOrganization,
  deleteOrganization,
  getOrganization,
  listOrganizations,
  updateOrganization
} from "@/lib/api/organizations";
import {
  getOrganizationModules,
  updateOrganizationModules
} from "@/lib/api/organization-modules";
import { ApiError, Organization, PageResult } from "@/lib/api/types";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/ui/password-input";
import { Select } from "@/components/ui/select";
import { Column, SortableChildTable } from "@/components/ui/sortable-child-table";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog";
import { useToast } from "@/components/ui/toaster";
import { useAccessToken } from "@/components/auth/use-access-token";

type Mode = "create" | "edit";
type StatusFilter = "active" | "archived" | "all";
type OrganizationViewMode = "grid" | "table";

const tokenStorageKey = "incoders.hive.apiToken";

export function OrganizationsClient() {
  const sessionToken = useAccessToken();
  const toast = useToast();
  const lastErrorToastRef = useRef<{ message: string; at: number } | null>(null);

  const [page, setPage] = useState<PageResult<Organization> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState<StatusFilter>("active");
  const [viewMode, setViewMode] = useState<OrganizationViewMode>("grid");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(12);
  const [manualToken, setManualToken] = useState("");
  const [tokenPanelOpen, setTokenPanelOpen] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [mode, setMode] = useState<Mode>("create");
  const [selected, setSelected] = useState<Organization | null>(null);
  const [details, setDetails] = useState<Organization | null>(null);
  const [name, setName] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [actingId, setActingId] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<Organization | null>(null);

  const items = useMemo(() => page?.items ?? [], [page?.items]);
  const effectiveToken = manualToken || (sessionToken ?? "");
  const stats = useMemo(() => {
    const active = items.filter((item) => item.isActive).length;
    return {
      visible: items.length,
      active,
      archived: items.length - active,
      total: page?.totalCount ?? 0
    };
  }, [items, page?.totalCount]);

  function pushErrorToast(message: string) {
    const now = Date.now();
    const lastToast = lastErrorToastRef.current;
    if (lastToast && lastToast.message === message && now - lastToast.at < 2500) {
      return;
    }

    lastErrorToastRef.current = { message, at: now };
    toast.error("Organizations request failed", message);
  }

  useEffect(() => {
    const storedToken = window.localStorage.getItem(tokenStorageKey) ?? "";
    setManualToken(storedToken === "__OPTIONAL_DEV_TOKEN__" ? "" : storedToken);
  }, []);

  useEffect(() => {
    if (manualToken) {
      window.localStorage.setItem(tokenStorageKey, manualToken);
    } else {
      window.localStorage.removeItem(tokenStorageKey);
    }
  }, [manualToken]);

  useEffect(() => {
    if (!effectiveToken) {
      setLoading(false);
      setPage(null);
      return;
    }

    void loadOrganizations();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageNumber, status, effectiveToken, sessionToken]);

  async function loadOrganizations(nextPage = pageNumber) {
    setLoading(true);
    setError(null);
    try {
      const data = await listOrganizations(
        {
          name: query || undefined,
          includeInactive: status !== "active",
          page: nextPage,
          pageSize
        },
        effectiveToken || undefined
      );

      const normalized =
        status === "archived"
          ? { ...data, items: data.items.filter((item) => !item.isActive) }
          : data;

      setPage(normalized);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      pushErrorToast(message);
    } finally {
      setLoading(false);
    }
  }

  async function refreshDetails(id: string) {
    try {
      const organization = await getOrganization(id, effectiveToken || undefined);
      setDetails(organization);
    } catch {
      setDetails(null);
    }
  }

  function openCreate() {
    setMode("create");
    setSelected(null);
    setName("");
    setFormOpen(true);
  }

  function openEdit(item: Organization) {
    setMode("edit");
    setSelected(item);
    setName(item.name);
    setFormOpen(true);
  }

  async function openDetails(item: Organization) {
    if (details?.id === item.id) {
      setDetails(null);
      return;
    }
    setDetails(item);
    await refreshDetails(item.id);
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    setSuccess(null);

    try {
      const saved =
        mode === "create"
          ? await createOrganization({ name }, effectiveToken || undefined)
          : selected
            ? await updateOrganization(selected.id, { name }, effectiveToken || undefined)
            : null;

      setSuccess(mode === "create" ? "Organization created." : "Organization updated.");
      toast.success(mode === "create" ? "Organization created" : "Organization updated", name.trim());
      setFormOpen(false);
      setName("");
      if (saved) {
        setDetails(saved);
      }
      await loadOrganizations();
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      pushErrorToast(message);
    } finally {
      setSubmitting(false);
    }
  }

  async function onArchive(item: Organization) {
    await runAction(item.id, async () => {
      const archived = await deactivateOrganization(item.id, effectiveToken || undefined);
      setSuccess("Organization archived.");
      toast.success("Organization archived", archived.name);
      setDetails(archived);
    });
  }

  async function onReactivate(item: Organization) {
    await runAction(item.id, async () => {
      const activated = await activateOrganization(item.id, effectiveToken || undefined);
      setSuccess("Organization reactivated.");
      toast.success("Organization reactivated", activated.name);
      setDetails(activated);
    });
  }

  function onRequestDelete(item: Organization) {
    setPendingDelete(item);
  }

  async function onConfirmDelete(item: Organization) {
    await runAction(item.id, async () => {
      const deleted = await deleteOrganization(item.id, effectiveToken || undefined);
      setSuccess("Organization deleted permanently.");
      toast.success(
        "Organization deleted permanently",
        `${deleted.name} • ${deleted.deletedClientCount} clients • ${deleted.deletedUserCount} users`
      );
      setDetails((current) => (current?.id === item.id ? null : current));
    });
    setPendingDelete(null);
  }

  async function runAction(id: string, action: () => Promise<void>) {
    setActingId(id);
    setError(null);
    setSuccess(null);
    try {
      await action();
      await loadOrganizations();
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      pushErrorToast(message);
    } finally {
      setActingId(null);
    }
  }

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPageNumber(1);
    void loadOrganizations(1);
  }

  return (
    <div className="space-y-6">
      <section className="relative overflow-hidden rounded-3xl border border-border bg-card p-6 shadow-2xl">
        <div className="absolute -right-20 -top-20 h-56 w-56 rounded-full bg-primary/20 blur-3xl" />
        <div className="absolute -bottom-24 left-1/3 h-56 w-56 rounded-full bg-emerald-400/10 blur-3xl" />
        <div className="relative flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
          <div className="max-w-3xl space-y-3">
            <Badge variant="success" className="w-fit">
              <Sparkles className="mr-1 h-3 w-3" /> Platform control plane
            </Badge>
            <div>
              <h1 className="text-3xl font-semibold tracking-tight md:text-5xl">Organizations cockpit</h1>
              <p className="mt-3 text-sm leading-6 text-muted-foreground md:text-base">
                End-to-end CRUD for the platform organization boundary. Every Social client, campaign,
                workflow and provider override hangs from this control plane.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button variant="outline" onClick={() => setTokenPanelOpen((value) => !value)}>
              <KeyRound className="h-4 w-4" /> API token
            </Button>
            <Button variant="outline" onClick={() => void loadOrganizations()}>
              <RefreshCcw className="h-4 w-4" /> Refresh
            </Button>
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> New organization
            </Button>
          </div>
        </div>
      </section>

      {tokenPanelOpen ? (
        <Card className="border-primary/40">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <ShieldCheck className="h-4 w-4 text-primary" /> API access
            </CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 lg:grid-cols-[1fr_auto]">
            <PasswordInput
              value={manualToken}
              onChange={(event) => setManualToken(event.target.value)}
              placeholder="Optional manual bearer token override"
            />
            <Button variant="secondary" onClick={() => setManualToken("")}>
              Clear token
            </Button>
            <p className="text-xs text-muted-foreground lg:col-span-2">
              Using {manualToken ? "manual token" : sessionToken ? "backend session token" : "no token"}.
            </p>
          </CardContent>
        </Card>
      ) : null}

      <div className="grid gap-3 md:grid-cols-4">
        <MetricCard icon={Building2} label="Visible" value={stats.visible} hint="Rows in current view" />
        <MetricCard icon={CheckCircle2} label="Active" value={stats.active} hint="Ready for workflows" />
        <MetricCard icon={Archive} label="Archived" value={stats.archived} hint="Recoverable records" />
        <MetricCard icon={Clock3} label="Total result" value={stats.total} hint="API total count" />
      </div>

      <Card>
        <CardContent className="p-4">
          <form className="grid gap-3 lg:grid-cols-[1fr_auto_auto]" onSubmit={applyFilters}>
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
              <Input
                className="pl-9"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Search organizations by name..."
              />
            </div>
            <StatusTabs value={status} onChange={setStatus} />
            <Button type="submit">Apply</Button>
          </form>
        </CardContent>
      </Card>

      {error ? <Notice tone="error" message={error} onDismiss={() => setError(null)} /> : null}
      {success ? <Notice tone="success" message={success} onDismiss={() => setSuccess(null)} /> : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_420px]">
        <Card className="overflow-hidden">
          <CardHeader className="border-b border-border">
            <CardTitle className="flex flex-wrap items-center justify-between gap-3 text-base">
              <span>{viewMode === "grid" ? "Organization grid" : "Organization table"}</span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant={viewMode === "grid" ? "default" : "outline"}
                  className="h-7 px-2"
                  title="Grid view"
                  onClick={() => setViewMode("grid")}
                >
                  <LayoutGrid className="h-4 w-4" />
                </Button>
                <Button
                  size="sm"
                  variant={viewMode === "table" ? "default" : "outline"}
                  className="h-7 px-2"
                  title="Table view"
                  onClick={() => setViewMode("table")}
                >
                  <List className="h-4 w-4" />
                </Button>
                <span className="text-xs font-normal text-muted-foreground">
                  Page {page?.page ?? pageNumber} of {Math.max(page?.totalPages ?? 1, 1)}
                </span>
              </div>
            </CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            {loading ? (
              viewMode === "grid" ? (
                <div className="grid gap-3 p-4 md:grid-cols-2">
                  {Array.from({ length: 4 }).map((_, index) => (
                    <div key={index} className="h-36 animate-pulse rounded-2xl border border-border bg-muted/40" />
                  ))}
                </div>
              ) : (
                <div className="p-4">
                  {Array.from({ length: 6 }).map((_, index) => (
                    <div key={index} className="mb-3 h-12 animate-pulse rounded-md border border-border bg-muted/40" />
                  ))}
                </div>
              )
            ) : items.length === 0 ? (
              <EmptyState onCreate={openCreate} />
            ) : viewMode === "grid" ? (
              <div className="grid gap-3 p-4 md:grid-cols-2">
                {items.map((item, index) => (
                  <motion.article
                    key={item.id}
                    initial={{ opacity: 0, y: 10 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: index * 0.03 }}
                    className={cn(
                      "group rounded-2xl border border-border bg-background/40 p-4 transition hover:border-primary/60 hover:bg-muted/30",
                      details?.id === item.id && "border-primary/80 bg-primary/10"
                    )}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="flex items-center gap-2">
                          <h2 className="font-semibold">{item.name}</h2>
                          <Badge variant={item.isActive ? "success" : "muted"}>
                            {item.isActive ? "Active" : "Archived"}
                          </Badge>
                        </div>
                        <p className="mt-2 max-w-[32rem] truncate text-xs text-muted-foreground">{item.id}</p>
                      </div>
                      <Button variant="outline" size="sm" onClick={() => void openDetails(item)}>
                        <Eye className="h-4 w-4" />
                      </Button>
                    </div>

                    <div className="mt-4 grid grid-cols-2 gap-2 text-xs text-muted-foreground">
                      <Fact label="Owner" value={shortId(item.ownerUserId)} />
                      <Fact label="Updated" value={formatDate(item.updatedAtUtc)} />
                    </div>

                    <div className="mt-4 flex flex-wrap gap-2">
                      <Button variant="secondary" size="sm" onClick={() => openEdit(item)}>
                        <Edit3 className="h-4 w-4" /> Edit
                      </Button>
                      {item.isActive ? (
                        <Button
                          variant="warning"
                          size="sm"
                          disabled={actingId === item.id}
                          onClick={() => void onArchive(item)}
                        >
                          <Archive className="h-4 w-4" /> Archive
                        </Button>
                      ) : (
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={actingId === item.id}
                          onClick={() => void onReactivate(item)}
                        >
                          <RotateCcw className="h-4 w-4" /> Reactivate
                        </Button>
                      )}
                      <Button
                        variant="destructive"
                        size="sm"
                        disabled={actingId === item.id}
                        onClick={() => onRequestDelete(item)}
                      >
                        <Trash2 className="h-4 w-4" /> Delete
                      </Button>
                    </div>
                  </motion.article>
                ))}
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Name</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Owner</TableHead>
                    <TableHead>Created</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((item) => (
                    <TableRow key={item.id} className={details?.id === item.id ? "bg-primary/10" : undefined}>
                      <TableCell className="font-medium">{item.name}</TableCell>
                      <TableCell>
                        <Badge variant={item.isActive ? "success" : "muted"}>
                          {item.isActive ? "Active" : "Archived"}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">{shortId(item.ownerUserId)}</TableCell>
                      <TableCell className="text-xs text-muted-foreground">{formatDate(item.createdAtUtc)}</TableCell>
                      <TableCell className="text-right">
                        <div className="inline-flex gap-2">
                          <Button variant="outline" size="sm" onClick={() => void openDetails(item)}>
                            <Eye className="h-4 w-4" /> Details
                          </Button>
                          <Button variant="secondary" size="sm" onClick={() => openEdit(item)}>
                            <Edit3 className="h-4 w-4" /> Edit
                          </Button>
                          {item.isActive ? (
                            <Button
                              variant="warning"
                              size="sm"
                              disabled={actingId === item.id}
                              onClick={() => void onArchive(item)}
                            >
                              Archive
                            </Button>
                          ) : (
                            <Button
                              variant="outline"
                              size="sm"
                              disabled={actingId === item.id}
                              onClick={() => void onReactivate(item)}
                            >
                              Reactivate
                            </Button>
                          )}
                          <Button
                            variant="destructive"
                            size="sm"
                            disabled={actingId === item.id}
                            onClick={() => onRequestDelete(item)}
                          >
                            Delete
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}

            <div className="flex items-center justify-between border-t border-border p-4">
              <Button
                variant="outline"
                disabled={!page?.hasPreviousPage || loading}
                onClick={() => setPageNumber((value) => Math.max(1, value - 1))}
              >
                <ChevronLeft className="h-4 w-4" /> Previous
              </Button>
              <span className="text-xs text-muted-foreground">
                {page?.totalCount ?? 0} organizations • {pageSize} per page
              </span>
              <Button
                variant="outline"
                disabled={!page?.hasNextPage || loading}
                onClick={() => setPageNumber((value) => value + 1)}
              >
                Next <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          </CardContent>
        </Card>

        <OrganizationDetailsPanel
          organization={details}
          token={effectiveToken || undefined}
          onEdit={(item) => openEdit(item)}
          onArchive={(item) => void onArchive(item)}
          onReactivate={(item) => void onReactivate(item)}
          onDelete={onRequestDelete}
          acting={actingId === details?.id}
        />
      </div>

      <Dialog open={formOpen} onOpenChange={setFormOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{mode === "create" ? "Create organization" : "Edit organization"}</DialogTitle>
            <DialogDescription>
              Manage the organization boundary used by clients, workflows, campaigns and provider preferences.
            </DialogDescription>
          </DialogHeader>
          <form className="space-y-4" onSubmit={onSubmit}>
            <div className="space-y-2">
              <label htmlFor="organization-name" className="text-sm font-medium">
                Name
              </label>
              <Input
                id="organization-name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="e.g. Acme Growth Studio"
                required
              />
            </div>
            <div className="rounded-xl border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
              Creating an organization also establishes the platform boundary used by Social clients, campaigns,
              workflows and provider preferences.
            </div>
            <div className="flex justify-end gap-2">
              <Button type="button" variant="secondary" onClick={() => setFormOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={submitting || !name.trim()}>
                {submitting ? "Saving..." : mode === "create" ? "Create" : "Save changes"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>

      <ConfirmationDialog
        open={pendingDelete !== null}
        title="Delete organization permanently?"
        description={
          pendingDelete
            ? `This will permanently delete "${pendingDelete.name}" and all organization-scoped data, including users, clients, accounts, brand manuals, plans, analyses, integrations, generated packages, and stored assets. This cannot be undone.`
            : ""
        }
        confirmLabel="Delete permanently"
        cancelLabel="Cancel"
        confirmVariant="destructive"
        icon={<Trash2 className="h-5 w-5" />}
        disableDismiss={pendingDelete !== null && actingId === pendingDelete.id}
        onConfirm={() => {
          if (pendingDelete) {
            void onConfirmDelete(pendingDelete);
          }
        }}
        onCancel={() => {
          if (!actingId) {
            setPendingDelete(null);
          }
        }}
      />
    </div>
  );
}

function MetricCard({
  icon: Icon,
  label,
  value,
  hint
}: {
  icon: typeof Building2;
  label: string;
  value: number;
  hint: string;
}) {
  return (
    <Card>
      <CardContent className="flex items-center gap-4 p-5">
        <div className="rounded-2xl border border-border bg-muted p-3">
          <Icon className="h-5 w-5 text-primary" />
        </div>
        <div>
          <p className="text-xs uppercase tracking-widest text-muted-foreground">{label}</p>
          <p className="text-2xl font-semibold">{value}</p>
          <p className="text-xs text-muted-foreground">{hint}</p>
        </div>
      </CardContent>
    </Card>
  );
}

function StatusTabs({ value, onChange }: { value: StatusFilter; onChange: (value: StatusFilter) => void }) {
  const options: Array<{ value: StatusFilter; label: string }> = [
    { value: "active", label: "Active" },
    { value: "archived", label: "Archived" },
    { value: "all", label: "All" }
  ];

  return (
    <div className="inline-flex rounded-md border border-border bg-background p-1">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          onClick={() => onChange(option.value)}
          className={cn(
            "rounded px-3 py-2 text-sm text-muted-foreground transition",
            value === option.value && "bg-muted text-foreground"
          )}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

// ── Connector mocks ───────────────────────────────────────────────────────────

interface OrgConnector {
  id: string;
  provider: string;
  providerLabel: string;
  model: string;
  isDefault: boolean;
  apiKeyMasked: string;
  status: "connected" | "disconnected" | "error";
}

interface OrganizationModuleOption {
  key: string;
  label: string;
  description: string;
  shortLabel: string;
}

const claudeModelOptions = [
  { value: "claude-opus-4-7", label: "Claude Opus 4.7" },
  { value: "claude-opus-4-5", label: "Claude Opus 4.5" },
  { value: "claude-sonnet-4-5", label: "Claude Sonnet 4.5" },
  { value: "claude-haiku-4-5", label: "Claude Haiku 4.5" }
];

function getDefaultConnectors(): OrgConnector[] {
  return [
    {
      id: "conn-claude-default",
      provider: "claude",
      providerLabel: "Anthropic Claude",
      model: "claude-opus-4-7",
      isDefault: true,
      apiKeyMasked: "sk-ant-••••••••••••••••••••••••••••••6Kzp",
      status: "connected"
    }
  ];
}

function OrganizationDetailsPanel({
  organization,
  token,
  onEdit,
  onArchive,
  onReactivate,
  onDelete,
  acting
}: {
  organization: Organization | null;
  token?: string;
  onEdit: (organization: Organization) => void;
  onArchive: (organization: Organization) => void;
  onReactivate: (organization: Organization) => void;
  onDelete: (organization: Organization) => void;
  acting: boolean;
}) {
  const [detailTab, setDetailTab] = useState<"overview" | "connectors" | "modules">("overview");
  const [connectors, setConnectors] = useState<OrgConnector[]>([]);
  const [addingConnector, setAddingConnector] = useState(false);
  const [newProvider, setNewProvider] = useState("claude");
  const [newModel, setNewModel] = useState("claude-opus-4-7");
  const [newApiKey, setNewApiKey] = useState("");
  const [testingId, setTestingId] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<Record<string, "ok" | "fail">>({});
  const [modulesLoading, setModulesLoading] = useState(false);
  const [modulesSaving, setModulesSaving] = useState(false);
  const [moduleError, setModuleError] = useState<string | null>(null);
  const [availableModules, setAvailableModules] = useState<OrganizationModuleOption[]>([]);
  const [assignedModuleKeys, setAssignedModuleKeys] = useState<string[]>([]);

  const assignedModuleItems = useMemo(() => {
    const byKey = new Map(availableModules.map((module) => [module.key, module]));
    return assignedModuleKeys
      .map((key) => byKey.get(key))
      .filter((module): module is OrganizationModuleOption => module !== undefined);
  }, [availableModules, assignedModuleKeys]);

  const moduleColumns: Column<OrganizationModuleOption>[] = [
    {
      key: "module",
      label: "Module",
      render: (module) => <span className="font-medium">{module.label}</span>,
    },
    {
      key: "description",
      label: "Description",
      render: (module) => <span className="text-xs text-muted-foreground">{module.description}</span>,
    },
  ];

  useEffect(() => {
    if (!organization) {
      setAvailableModules([]);
      setAssignedModuleKeys([]);
      setModuleError(null);
      return;
    }

    if (organization) {
      setConnectors(getDefaultConnectors());
      setDetailTab("overview");
    }

    if (!token) {
      setAvailableModules([]);
      setAssignedModuleKeys([]);
      setModuleError(null);
      return;
    }

    let cancelled = false;
    setModulesLoading(true);
    setModuleError(null);

    void (async () => {
      try {
        const response = await getOrganizationModules(organization.id, token);
        if (cancelled) return;

        const nextAvailableModules = response.availableModules.map((module) => ({
          key: module.moduleKey,
          label: module.displayName,
          description: module.description,
          shortLabel: module.shortLabel,
        }));
        const nextAssignedKeys = response.assignedModules
          .sort((left, right) => left.sortOrder - right.sortOrder)
          .map((module) => module.moduleKey);

        setAvailableModules(nextAvailableModules);
        setAssignedModuleKeys(nextAssignedKeys);
      } catch (err) {
        if (cancelled) return;
        setModuleError((err as ApiError).message ?? "Could not load organization modules.");
        setAvailableModules([]);
        setAssignedModuleKeys([]);
      } finally {
        if (!cancelled) {
          setModulesLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [organization, token]);

  function toggleAssignedModule(moduleKey: string) {
    setAssignedModuleKeys((current) =>
      current.includes(moduleKey)
        ? current.filter((key) => key !== moduleKey)
        : [...current, moduleKey]
    );
  }

  async function handleSaveModules() {
    if (!organization || !token) return;

    setModulesSaving(true);
    setModuleError(null);
    try {
      const response = await updateOrganizationModules(organization.id, assignedModuleKeys, token);
      const nextAvailableModules = response.availableModules.map((module) => ({
        key: module.moduleKey,
        label: module.displayName,
        description: module.description,
        shortLabel: module.shortLabel,
      }));
      const nextAssignedKeys = response.assignedModules
        .sort((left, right) => left.sortOrder - right.sortOrder)
        .map((module) => module.moduleKey);

      setAvailableModules(nextAvailableModules);
      setAssignedModuleKeys(nextAssignedKeys);
    } catch (err) {
      setModuleError((err as ApiError).message ?? "Could not save organization modules.");
    } finally {
      setModulesSaving(false);
    }
  }

  async function handleTestConnection(connector: OrgConnector) {
    setTestingId(connector.id);
    await new Promise((r) => setTimeout(r, 1400));
    setTestResult((prev) => ({ ...prev, [connector.id]: "ok" }));
    setTestingId(null);
  }

  async function handleAddConnector() {
    if (!newApiKey.trim()) return;
    const masked = `${newApiKey.slice(0, 8)}••••••••••••••••${newApiKey.slice(-4)}`;
    const newConn: OrgConnector = {
      id: `conn-${Date.now()}`,
      provider: newProvider,
      providerLabel: "Anthropic Claude",
      model: newModel,
      isDefault: connectors.length === 0,
      apiKeyMasked: masked,
      status: "disconnected"
    };
    setConnectors((prev) => [...prev, newConn]);
    setNewApiKey("");
    setAddingConnector(false);
  }

  function handleRemoveConnector(id: string) {
    setConnectors((prev) => prev.filter((c) => c.id !== id));
  }

  function handleSetDefault(id: string) {
    setConnectors((prev) => prev.map((c) => ({ ...c, isDefault: c.id === id })));
  }

  if (!organization) {
    return (
      <Card className="min-h-[28rem]">
        <CardContent className="flex h-full min-h-[28rem] flex-col items-center justify-center p-8 text-center">
          <div className="mb-4 rounded-3xl border border-border bg-muted p-5">
            <Building2 className="h-8 w-8 text-primary" />
          </div>
          <h2 className="text-lg font-semibold">Select an organization</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            Inspect the platform boundary before creating clients, campaigns and social workflows.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="sticky top-6 min-h-[28rem]">
      <CardHeader className="border-b border-border">
        <div className="flex items-start justify-between gap-3">
          <div>
            <CardTitle>{organization.name}</CardTitle>
            <p className="mt-1 text-xs text-muted-foreground">{organization.id}</p>
          </div>
          <Badge variant={organization.isActive ? "success" : "muted"}>
            {organization.isActive ? "Active" : "Archived"}
          </Badge>
        </div>
        <div className="mt-3 inline-flex rounded-md border border-border bg-background p-1">
          {(["overview", "connectors", "modules"] as const).map((tab) => (
            <button
              key={tab}
              type="button"
              onClick={() => setDetailTab(tab)}
              className={cn(
                "rounded px-3 py-1.5 text-xs font-medium capitalize transition",
                detailTab === tab ? "bg-muted text-foreground" : "text-muted-foreground hover:text-foreground"
              )}
            >
              {tab === "connectors"
                ? `Connectors (${connectors.length})`
                : tab === "modules"
                  ? `Modules (${assignedModuleKeys.length})`
                  : "Overview"}
            </button>
          ))}
        </div>
      </CardHeader>

      <CardContent className="space-y-5 p-6">
        {detailTab === "overview" && (
          <>
            <div className="grid gap-3">
              <Fact label="Owner user" value={organization.ownerUserId} />
              <Fact label="Created" value={formatDateTime(organization.createdAtUtc)} />
              <Fact label="Updated" value={formatDateTime(organization.updatedAtUtc)} />
              <Fact label="Archived at" value={organization.deactivatedAtUtc ? formatDateTime(organization.deactivatedAtUtc) : "—"} />
            </div>

            <div className="rounded-2xl border border-border bg-muted/30 p-4">
              <p className="text-sm font-medium">Next module links</p>
              <p className="mt-2 text-sm text-muted-foreground">
                Social clients, campaigns, content sources and provider overrides must reference this organization.
              </p>
            </div>

            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => onEdit(organization)}>
                <Edit3 className="h-4 w-4" /> Edit
              </Button>
              {organization.isActive ? (
                <Button variant="warning" disabled={acting} onClick={() => onArchive(organization)}>
                  <Archive className="h-4 w-4" /> Archive
                </Button>
              ) : (
                <Button variant="outline" disabled={acting} onClick={() => onReactivate(organization)}>
                  <RotateCcw className="h-4 w-4" /> Reactivate
                </Button>
              )}
              <Button variant="destructive" disabled={acting} onClick={() => onDelete(organization)}>
                <Trash2 className="h-4 w-4" /> Delete
              </Button>
            </div>
          </>
        )}

        {detailTab === "connectors" && (
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <p className="text-sm text-muted-foreground">AI provider connectors for this organization.</p>
              <Button size="sm" variant="outline" onClick={() => setAddingConnector((v) => !v)}>
                <Plus className="h-4 w-4" /> Add connector
              </Button>
            </div>

            {addingConnector && (
              <div className="rounded-xl border border-border bg-muted/30 p-4 space-y-3">
                <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">New connector</p>
                <div className="space-y-1">
                  <label className="text-xs text-muted-foreground">Provider</label>
                  <Select
                    size="sm"
                    className="w-full"
                    value={newProvider}
                    onChange={(e) => setNewProvider(e.target.value)}
                  >
                    <option value="claude">Anthropic Claude</option>
                    <option value="openai">OpenAI</option>
                    <option value="gemini">Google Gemini</option>
                  </Select>
                </div>
                {newProvider === "claude" && (
                  <div className="space-y-1">
                    <label className="text-xs text-muted-foreground">Model</label>
                    <Select
                      size="sm"
                      className="w-full"
                      value={newModel}
                      onChange={(e) => setNewModel(e.target.value)}
                    >
                      {claudeModelOptions.map((m) => (
                        <option key={m.value} value={m.value}>{m.label}</option>
                      ))}
                    </Select>
                  </div>
                )}
                <div className="space-y-1">
                  <label className="text-xs text-muted-foreground">API Key</label>
                  <Input
                    type="password"
                    placeholder="sk-ant-…"
                    value={newApiKey}
                    onChange={(e) => setNewApiKey(e.target.value)}
                    className="h-8 text-xs"
                  />
                </div>
                <div className="flex gap-2">
                  <Button size="sm" onClick={() => void handleAddConnector()} disabled={!newApiKey.trim()} className="h-7 text-xs">
                    Save connector
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => setAddingConnector(false)} className="h-7 text-xs">
                    Cancel
                  </Button>
                </div>
              </div>
            )}

            {connectors.length === 0 ? (
              <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-border py-8 text-center text-sm text-muted-foreground space-y-2">
                <BrainCircuit className="h-8 w-8 opacity-30" />
                <p>No connectors configured.</p>
                <p className="text-xs">Add a Claude connector to enable AI features for this org.</p>
              </div>
            ) : (
              <div className="space-y-3">
                {connectors.map((conn) => {
                  const isOk = testResult[conn.id] === "ok";
                  const isFail = testResult[conn.id] === "fail";
                  const isTesting = testingId === conn.id;
                  return (
                    <div key={conn.id} className="rounded-xl border border-border bg-background/60 p-4 space-y-3">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-2">
                          <BrainCircuit className="h-4 w-4 text-purple-400 shrink-0" />
                          <div>
                            <p className="text-sm font-medium">{conn.providerLabel}</p>
                            <p className="text-xs text-muted-foreground">{conn.model}</p>
                          </div>
                        </div>
                        <div className="flex items-center gap-1.5">
                          {conn.isDefault && (
                            <Badge variant="success" className="text-[10px]">Default</Badge>
                          )}
                          <Badge
                            variant={conn.status === "connected" || isOk ? "success" : isFail ? "destructive" : "muted"}
                            className="text-[10px]"
                          >
                            {isTesting ? "Testing…" : isOk ? "Connected" : isFail ? "Error" : conn.status}
                          </Badge>
                        </div>
                      </div>
                      <div className="rounded-md border border-border bg-muted/30 px-3 py-2 font-mono text-xs text-muted-foreground">
                        {conn.apiKeyMasked}
                      </div>
                      <div className="flex flex-wrap gap-2">
                        <Button
                          size="sm"
                          variant="outline"
                          className="h-7 text-xs"
                          disabled={isTesting}
                          onClick={() => void handleTestConnection(conn)}
                        >
                          {isTesting ? (
                            <><Loader2 className="h-3 w-3 animate-spin" /> Testing…</>
                          ) : (
                            <><Zap className="h-3 w-3" /> Test connection</>
                          )}
                        </Button>
                        {!conn.isDefault && (
                          <Button size="sm" variant="ghost" className="h-7 text-xs" onClick={() => handleSetDefault(conn.id)}>
                            Set as default
                          </Button>
                        )}
                        <Button size="sm" variant="ghost" className="h-7 text-xs text-destructive hover:text-destructive" onClick={() => handleRemoveConnector(conn.id)}>
                          <Trash2 className="h-3 w-3" /> Remove
                        </Button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        )}

        {detailTab === "modules" && (
          <div className="space-y-4">
            <p className="text-sm text-muted-foreground">
              Assign system modules to this organization and define selector order. Changes are persisted only when you click save.
            </p>

            {moduleError ? (
              <div className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive">
                {moduleError}
              </div>
            ) : null}

            {modulesLoading ? (
              <p className="text-xs text-muted-foreground">Loading organization modules...</p>
            ) : (
              <>
                <div className="flex flex-wrap gap-3">
                  {availableModules.map((module) => (
                    <label key={module.key} className="flex items-center gap-2 text-sm text-muted-foreground">
                      <input
                        type="checkbox"
                        checked={assignedModuleKeys.includes(module.key)}
                        onChange={() => toggleAssignedModule(module.key)}
                        className="h-4 w-4 rounded border-border"
                      />
                      {module.label}
                    </label>
                  ))}
                </div>

                <SortableChildTable<OrganizationModuleOption>
                  items={assignedModuleItems}
                  getId={(item) => item.key}
                  columns={moduleColumns}
                  onReorder={(orderedIds) => setAssignedModuleKeys(orderedIds)}
                  primaryLabel="Primary"
                  emptyState="No modules assigned to this organization."
                />

                <div className="flex justify-end gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    disabled={modulesLoading || modulesSaving}
                    onClick={() => setAssignedModuleKeys([])}
                  >
                    Clear
                  </Button>
                  <Button
                    type="button"
                    disabled={modulesLoading || modulesSaving || !token}
                    onClick={() => void handleSaveModules()}
                  >
                    {modulesSaving ? (
                      <>
                        <Loader2 className="h-4 w-4 animate-spin" /> Saving...
                      </>
                    ) : (
                      "Save modules"
                    )}
                  </Button>
                </div>
              </>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function EmptyState({ onCreate }: { onCreate: () => void }) {
  return (
    <div className="flex min-h-72 flex-col items-center justify-center p-8 text-center">
      <div className="mb-4 rounded-3xl border border-border bg-muted p-5">
        <Building2 className="h-8 w-8 text-primary" />
      </div>
      <h2 className="text-lg font-semibold">No organizations in this view</h2>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">
        Create the first organization to unlock client management and the Social workflow.
      </p>
      <Button className="mt-5" onClick={onCreate}>
        <Plus className="h-4 w-4" /> Create organization
      </Button>
    </div>
  );
}

function Notice({ tone, message, onDismiss }: { tone: "error" | "success"; message: string; onDismiss: () => void }) {
  return (
    <div
      className={cn(
        "flex items-center justify-between rounded-xl border p-4 text-sm",
        tone === "error" ? "border-red-500/40 bg-red-500/10 text-red-700 dark:text-red-200" : "border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-200"
      )}
    >
      <span>{tone === "error" ? `Error: ${message}` : message}</span>
      <button type="button" onClick={onDismiss} className="opacity-70 hover:opacity-100">
        <X className="h-4 w-4" />
      </button>
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border bg-background/40 p-3">
      <p className="text-[0.65rem] uppercase tracking-widest text-muted-foreground">{label}</p>
      <p className="mt-1 break-all text-sm">{value}</p>
    </div>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric" }).format(new Date(value));
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

function shortId(value: string) {
  return `${value.slice(0, 8)}…${value.slice(-4)}`;
}
