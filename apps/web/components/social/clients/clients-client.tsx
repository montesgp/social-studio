"use client";

import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import {
  Archive,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock3,
  Edit3,
  Eye,
  LayoutGrid,
  LayoutList,
  Plus,
  RefreshCcw,
  RotateCcw,
  Search,
  Sparkles,
  Trash2,
  Users,
  X
} from "lucide-react";
import {
  activateSocialClient,
  deleteSocialClient,
  deactivateSocialClient,
  getSocialClient,
  listSocialClients
} from "@/lib/api/social-clients";
import { listOrganizations } from "@/lib/api/organizations";
import { ApiError, Organization, PageResult, SocialClient } from "@/lib/api/types";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog";
import { useToast } from "@/components/ui/toaster";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { ClientBrandManualSection } from "@/components/social/clients/client-brand-manual-section";
import { ClientFormPanel } from "@/components/social/clients/client-form-panel";
import { useClientForm } from "@/components/social/clients/use-client-form";

type StatusFilter = "active" | "archived" | "all";
type ViewMode = "grid" | "table";

interface ClientsClientProps {
  isPlatformAdmin: boolean;
}

export function ClientsClient({ isPlatformAdmin }: ClientsClientProps) {
  const sessionToken = useAccessToken();
  const { orgId, isInitialized: orgInitialized } = useActiveOrg();
  const toast = useToast();
  const lastErrorToastRef = useRef<{ message: string; at: number } | null>(null);

  const [page, setPage] = useState<PageResult<SocialClient> | null>(null);
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState<StatusFilter>("active");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(12);
  const [details, setDetails] = useState<SocialClient | null>(null);
  const [actingId, setActingId] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<ViewMode>("grid");
  const [pendingDelete, setPendingDelete] = useState<SocialClient | null>(null);

  const effectiveToken = sessionToken ?? "";
  const items = useMemo(() => page?.items ?? [], [page]);

  const organizationNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const organization of organizations) {
      map.set(organization.id, organization.name);
    }
    return map;
  }, [organizations]);

  const {
    formOpen,
    mode,
    selected,
    name,
    description,
    sectorOrNiche,
    backgroundSourcingPolicy,
    aestheticRefreshThresholdDays,
    fieldErrors,
    referentAccounts,
    referentAccountsLoading,
    competitorAccounts,
    competitorAccountsLoading,
    ownAccounts,
    ownAccountsLoading,
    submitting,
    setName,
    setDescription,
    setSectorOrNiche,
    setBackgroundSourcingPolicy,
    setAestheticRefreshThresholdDays,
    setReferentAccounts,
    setCompetitorAccounts,
    setOwnAccounts,
    openCreate,
    openEdit,
    closeForm,
    onSubmit,
  } = useClientForm({
    effectiveToken,
    onSuccess: (saved, formMode) => {
      setSuccess(formMode === "create" ? "Client created." : "Client updated.");
      toast.success(formMode === "create" ? "Client created" : "Client updated", saved.name);
      setDetails(saved);
      void loadClients();
    },
    onError: (message) => {
      setError(message);
      pushErrorToast(message);
    },
  });
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
    toast.error("Clients request failed", message);
  }

  useEffect(() => {
    if (!orgInitialized) return;
    setPageNumber(1);
    setDetails(null);
  }, [orgId, orgInitialized]);

  useEffect(() => {
    if (!orgInitialized || !effectiveToken || !isPlatformAdmin) {
      setOrganizations([]);
      return;
    }

    void loadOrganizations();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [effectiveToken, isPlatformAdmin, orgInitialized]);

  useEffect(() => {
    if (!orgInitialized) return;

    if (!effectiveToken || (!isPlatformAdmin && !orgId)) {
      setLoading(false);
      setPage(null);
      setDetails(null);
      return;
    }

    void loadClients();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageNumber, status, effectiveToken, sessionToken, orgInitialized, orgId, isPlatformAdmin, selectedOrganizationId]);

  async function loadOrganizations() {
    try {
      const data = await listOrganizations(
        { includeInactive: false, page: 1, pageSize: 100 },
        effectiveToken || undefined
      );
      setOrganizations([...data.items].sort((a, b) => a.name.localeCompare(b.name)));
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      pushErrorToast(message);
    }
  }

  async function loadClients(nextPage = pageNumber) {
    setLoading(true);
    setError(null);
    try {
      const data = await listSocialClients(
        {
          organizationId: isPlatformAdmin && selectedOrganizationId ? selectedOrganizationId : undefined,
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
      setDetails((current) => {
        if (!current) return null;
        return normalized.items.find((item) => item.id === current.id) ?? null;
      });
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
      const client = await getSocialClient(id, effectiveToken || undefined);
      setDetails(client);
    } catch {
      setDetails(null);
    }
  }

  async function openDetails(item: SocialClient) {
    setDetails(item);
    await refreshDetails(item.id);
  }

  async function onArchive(item: SocialClient) {
    await runAction(item.id, async () => {
      const archived = await deactivateSocialClient(item.id, effectiveToken || undefined);
      setSuccess("Client archived.");
      toast.success("Client archived", archived.name);
      setDetails(archived);
    });
  }

  async function onReactivate(item: SocialClient) {
    await runAction(item.id, async () => {
      const activated = await activateSocialClient(item.id, effectiveToken || undefined);
      setSuccess("Client reactivated.");
      toast.success("Client reactivated", activated.name);
      setDetails(activated);
    });
  }

  function onRequestDelete(item: SocialClient) {
    setPendingDelete(item);
  }

  async function onConfirmDelete(item: SocialClient) {
    await runAction(item.id, async () => {
      const deleted = await deleteSocialClient(item.id, effectiveToken || undefined);
      setSuccess("Client deleted permanently.");
      toast.success("Client deleted permanently", deleted.name);
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
      await loadClients();
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
    void loadClients(1);
  }

  function resolveOrganizationName(client: SocialClient) {
    return client.organizationName ?? organizationNameById.get(client.organizationId) ?? client.organizationId;
  }

  function changeOrganizationFilter(organizationId: string) {
    setSelectedOrganizationId(organizationId);
    setPageNumber(1);
    setDetails(null);
  }

  return (
    <div className="space-y-6">
      <section className="relative overflow-hidden rounded-3xl border border-border bg-card p-6 shadow-2xl">
        <div className="absolute -right-20 -top-20 h-56 w-56 rounded-full bg-primary/20 blur-3xl" />
        <div className="absolute -bottom-24 left-1/3 h-56 w-56 rounded-full bg-emerald-400/10 blur-3xl" />
        <div className="relative flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
          <div className="max-w-3xl space-y-3">
            <Badge variant="success" className="w-fit">
              <Sparkles className="mr-1 h-3 w-3" /> Social clients
            </Badge>
            <div>
              <h1 className="text-3xl font-semibold tracking-tight md:text-5xl">Clients cockpit</h1>
              <p className="mt-3 text-sm leading-6 text-muted-foreground md:text-base">
                {isPlatformAdmin
                  ? "Manage Social customers across organizations."
                  : "Manage Social customers under the resolved organization."}
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button variant="outline" onClick={() => void loadClients()}>
              <RefreshCcw className="h-4 w-4" /> Refresh
            </Button>
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> New client
            </Button>
          </div>
        </div>
      </section>

      <AnimatePresence>
        {formOpen && (
          <motion.div
            key="inline-form"
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: "auto" }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.2 }}
            className="overflow-hidden"
          >
            <Card>
              <CardHeader>
                <CardTitle>{mode === "create" ? "New client" : "Edit client"}</CardTitle>
              </CardHeader>
              <CardContent>
                <ClientFormPanel
                  mode={mode}
                  clientId={selected?.id ?? null}
                  name={name}
                  description={description}
                  sectorOrNiche={sectorOrNiche}
                  backgroundSourcingPolicy={backgroundSourcingPolicy}
                  aestheticRefreshThresholdDays={aestheticRefreshThresholdDays}
                  fieldErrors={fieldErrors}
                  referentAccounts={referentAccounts}
                  referentAccountsLoading={referentAccountsLoading}
                  competitorAccounts={competitorAccounts}
                  competitorAccountsLoading={competitorAccountsLoading}
                  ownAccounts={ownAccounts}
                  ownAccountsLoading={ownAccountsLoading}
                  submitting={submitting}
                  onNameChange={setName}
                  onDescriptionChange={setDescription}
                  onSectorOrNicheChange={setSectorOrNiche}
                  onBackgroundSourcingPolicyChange={setBackgroundSourcingPolicy}
                  onAestheticRefreshThresholdDaysChange={setAestheticRefreshThresholdDays}
                  onReferentAccountsChange={setReferentAccounts}
                  onCompetitorAccountsChange={setCompetitorAccounts}
                  onOwnAccountsChange={setOwnAccounts}
                  onSubmit={onSubmit}
                  onCancel={() => {
                    closeForm();
                    setDetails(details);
                  }}
                  token={effectiveToken}
                />
              </CardContent>
            </Card>
          </motion.div>
        )}
      </AnimatePresence>

      <div className="grid gap-3 md:grid-cols-4">
        <MetricCard icon={Users} label="Visible" value={stats.visible} hint="Rows in current view" />
        <MetricCard icon={CheckCircle2} label="Active" value={stats.active} hint="Ready for campaigns" />
        <MetricCard icon={Archive} label="Archived" value={stats.archived} hint="Recoverable records" />
        <MetricCard icon={Clock3} label="Total result" value={stats.total} hint="API total count" />
      </div>

      <Card>
        <CardContent className="p-4">
          <form className="grid gap-3 lg:grid-cols-[1fr_auto_auto_auto]" onSubmit={applyFilters}>
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
              <Input
                className="pl-9"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Search clients by name..."
              />
            </div>
            {isPlatformAdmin ? (
              <Select
                className="min-w-[220px]"
                value={selectedOrganizationId}
                onChange={(event) => changeOrganizationFilter(event.target.value)}
              >
                <option value="">All organizations</option>
                {organizations.map((organization) => (
                  <option key={organization.id} value={organization.id}>
                    {organization.name}
                  </option>
                ))}
              </Select>
            ) : null}
            <StatusTabs value={status} onChange={setStatus} />
            <Button type="submit">Apply</Button>
          </form>
        </CardContent>
      </Card>

      {error ? <Notice tone="error" message={error} onDismiss={() => setError(null)} /> : null}
      {success ? <Notice tone="success" message={success} onDismiss={() => setSuccess(null)} /> : null}

      <div className={cn("grid gap-6", !formOpen && "xl:grid-cols-[1fr_420px]")}>
        <Card className="overflow-hidden">
          <CardHeader className="border-b border-border">
            <CardTitle className="flex items-center justify-between text-base">
              <span>Clients</span>
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
                  <LayoutList className="h-4 w-4" />
                </Button>
                <span className="text-xs font-normal text-muted-foreground">
                  Page {page?.page ?? pageNumber} of {Math.max(page?.totalPages ?? 1, 1)}
                </span>
              </div>
            </CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            {viewMode === "grid" ? (
              loading ? (
                <div className="grid gap-3 p-4 md:grid-cols-2">
                  {Array.from({ length: 4 }).map((_, index) => (
                    <div key={index} className="h-36 animate-pulse rounded-2xl border border-border bg-muted/40" />
                  ))}
                </div>
              ) : items.length === 0 ? (
                <EmptyState onCreate={openCreate} />
              ) : (
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
                          {isPlatformAdmin ? (
                            <p className="mt-1 text-xs text-muted-foreground">{resolveOrganizationName(item)}</p>
                          ) : null}
                          {item.description ? (
                            <p className="mt-1 max-w-[32rem] truncate text-xs text-muted-foreground">{item.description}</p>
                          ) : (
                            <p className="mt-2 max-w-[32rem] truncate text-xs text-muted-foreground">{item.id}</p>
                          )}
                        </div>
                        <Button variant="outline" size="sm" onClick={() => void openDetails(item)}>
                          <Eye className="h-4 w-4" />
                        </Button>
                      </div>

                      <div className="mt-4 grid grid-cols-2 gap-2 text-xs text-muted-foreground">
                        {isPlatformAdmin ? <Fact label="Organization" value={resolveOrganizationName(item)} /> : null}
                        <Fact label="Updated" value={formatDate(item.updatedAtUtc)} />
                        <Fact label="Created" value={formatDate(item.createdAtUtc)} />
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
                        {isPlatformAdmin ? (
                          <Button
                            variant="destructive"
                            size="sm"
                            disabled={actingId === item.id}
                            onClick={() => onRequestDelete(item)}
                          >
                            <Trash2 className="h-4 w-4" /> Delete
                          </Button>
                        ) : null}
                      </div>
                    </motion.article>
                  ))}
                </div>
              )
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Name</TableHead>
                    {isPlatformAdmin ? <TableHead>Organization</TableHead> : null}
                    <TableHead>Status</TableHead>
                    <TableHead>Description</TableHead>
                    <TableHead>Created</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {loading ? (
                    Array.from({ length: 4 }).map((_, index) => (
                      <TableRow key={index}>
                        {Array.from({ length: isPlatformAdmin ? 6 : 5 }).map((__, col) => (
                          <TableCell key={col}>
                            <div className="h-4 animate-pulse rounded bg-muted/40" />
                          </TableCell>
                        ))}
                      </TableRow>
                    ))
                  ) : items.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={isPlatformAdmin ? 6 : 5} className="py-12 text-center text-sm text-muted-foreground">
                        No clients in this view.
                      </TableCell>
                    </TableRow>
                  ) : (
                    items.map((item) => (
                      <TableRow key={item.id}>
                        <TableCell className="font-medium">{item.name}</TableCell>
                        {isPlatformAdmin ? (
                          <TableCell className="text-xs text-muted-foreground">{resolveOrganizationName(item)}</TableCell>
                        ) : null}
                        <TableCell>
                          <Badge variant={item.isActive ? "success" : "muted"}>{item.isActive ? "Active" : "Archived"}</Badge>
                        </TableCell>
                        <TableCell className="max-w-[200px] truncate text-xs text-muted-foreground">
                          {item.description ?? "—"}
                        </TableCell>
                        <TableCell className="text-xs text-muted-foreground">{formatDate(item.createdAtUtc)}</TableCell>
                        <TableCell className="text-right">
                          <div className="inline-flex gap-2">
                            <Button variant="outline" size="sm" onClick={() => void openDetails(item)}>
                              Details
                            </Button>
                            <Button variant="secondary" size="sm" onClick={() => openEdit(item)}>
                              Edit
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
                            {isPlatformAdmin ? (
                              <Button
                                variant="destructive"
                                size="sm"
                                disabled={actingId === item.id}
                                onClick={() => onRequestDelete(item)}
                              >
                                Delete
                              </Button>
                            ) : null}
                          </div>
                        </TableCell>
                      </TableRow>
                    ))
                  )}
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
                {page?.totalCount ?? 0} clients • {pageSize} per page
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

        {!formOpen && (
          <ClientDetailsPanel
            client={details}
            organizationName={details && isPlatformAdmin ? resolveOrganizationName(details) : null}
            onEdit={(item) => openEdit(item)}
            onArchive={(item) => void onArchive(item)}
            onReactivate={(item) => void onReactivate(item)}
            onDelete={onRequestDelete}
            canDelete={isPlatformAdmin}
            acting={actingId === details?.id}
          />
        )}
      </div>

      <ConfirmationDialog
        open={pendingDelete !== null}
        title="Delete client permanently?"
        description={
          pendingDelete
            ? `This will permanently delete "${pendingDelete.name}" and all client-scoped data, including accounts, brand manuals, plans, analyses, generated packages, and stored assets. This cannot be undone.`
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
  icon: typeof Users;
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
        <Button
          key={option.value}
          type="button"
          variant={value === option.value ? "secondary" : "ghost"}
          size="sm"
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </Button>
      ))}
    </div>
  );
}

function ClientDetailsPanel({
  client,
  organizationName,
  onEdit,
  onArchive,
  onReactivate,
  onDelete,
  canDelete,
  acting
}: {
  client: SocialClient | null;
  organizationName: string | null;
  onEdit: (client: SocialClient) => void;
  onArchive: (client: SocialClient) => void;
  onReactivate: (client: SocialClient) => void;
  onDelete: (client: SocialClient) => void;
  canDelete: boolean;
  acting: boolean;
}) {
  if (!client) {
    return (
      <Card className="min-h-[28rem]">
        <CardContent className="flex h-full min-h-[28rem] flex-col items-center justify-center p-8 text-center">
          <div className="mb-4 rounded-3xl border border-border bg-muted p-5">
            <Users className="h-8 w-8 text-primary" />
          </div>
          <h2 className="text-lg font-semibold">Select a client</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            View client details, edit profile, or manage lifecycle status.
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
            <CardTitle>{client.name}</CardTitle>
            <p className="mt-1 text-xs text-muted-foreground">{client.id}</p>
          </div>
          <Badge variant={client.isActive ? "success" : "muted"}>
            {client.isActive ? "Active" : "Archived"}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-5 p-6">
        <div className="grid gap-3">
          {organizationName ? <Fact label="Organization" value={organizationName} /> : null}
          {client.description ? <Fact label="Description" value={client.description} /> : null}
          <Fact label="Created" value={formatDateTime(client.createdAtUtc)} />
          <Fact label="Updated" value={formatDateTime(client.updatedAtUtc)} />
        </div>

        <ClientBrandManualSection clientId={client.id} />

        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => onEdit(client)}>
            <Edit3 className="h-4 w-4" /> Edit
          </Button>
          {client.isActive ? (
            <Button variant="warning" disabled={acting} onClick={() => onArchive(client)}>
              <Archive className="h-4 w-4" /> Archive
            </Button>
          ) : (
            <Button variant="outline" disabled={acting} onClick={() => onReactivate(client)}>
              <RotateCcw className="h-4 w-4" /> Reactivate
            </Button>
          )}
          {canDelete ? (
            <Button variant="destructive" disabled={acting} onClick={() => onDelete(client)}>
              <Trash2 className="h-4 w-4" /> Delete
            </Button>
          ) : null}
        </div>
      </CardContent>
    </Card>
  );
}

function EmptyState({ onCreate }: { onCreate: () => void }) {
  return (
    <div className="flex min-h-72 flex-col items-center justify-center p-8 text-center">
      <div className="mb-4 rounded-3xl border border-border bg-muted p-5">
        <Users className="h-8 w-8 text-primary" />
      </div>
      <h2 className="text-lg font-semibold">No clients in this view</h2>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">
        Create the first client to start managing Social campaigns and workflows.
      </p>
      <Button className="mt-5" onClick={onCreate}>
        <Plus className="h-4 w-4" /> Create client
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
      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={onDismiss}
        className="h-7 px-2 opacity-70 hover:opacity-100"
      >
        <X className="h-4 w-4" />
      </Button>
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
