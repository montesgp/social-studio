"use client";

import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { motion } from "framer-motion";
import {
  Archive,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock3,
  Edit3,
  Eye,
  Plus,
  RefreshCcw,
  RotateCcw,
  Search,
  Sparkles,
  Users,
  X
} from "lucide-react";
import {
  activateSocialClient,
  createSocialClient,
  deactivateSocialClient,
  getSocialClient,
  listSocialClients,
  updateSocialClient
} from "@/lib/api/social-clients";
import { ApiError, PageResult, SocialClient } from "@/lib/api/types";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useToast } from "@/components/ui/toaster";
import { useAccessToken } from "@/components/auth/use-access-token";
import { ClientReferentAccountsSection } from "@/components/social/clients/client-referent-accounts-section";

type Mode = "create" | "edit";
type StatusFilter = "active" | "archived" | "all";

export function ClientsClient() {
  const sessionToken = useAccessToken();
  const toast = useToast();
  const lastErrorToastRef = useRef<{ message: string; at: number } | null>(null);

  const [page, setPage] = useState<PageResult<SocialClient> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState<StatusFilter>("active");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(12);
  const [formOpen, setFormOpen] = useState(false);
  const [mode, setMode] = useState<Mode>("create");
  const [selected, setSelected] = useState<SocialClient | null>(null);
  const [details, setDetails] = useState<SocialClient | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [actingId, setActingId] = useState<string | null>(null);

  const items = page?.items ?? [];
  const effectiveToken = sessionToken ?? "";
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
    if (!effectiveToken) {
      setLoading(false);
      setPage(null);
      return;
    }

    void loadClients();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageNumber, status, effectiveToken, sessionToken]);

  async function loadClients(nextPage = pageNumber) {
    setLoading(true);
    setError(null);
    try {
      const data = await listSocialClients(
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
      const client = await getSocialClient(id, effectiveToken || undefined);
      setDetails(client);
    } catch {
      setDetails(null);
    }
  }

  function openCreate() {
    setMode("create");
    setSelected(null);
    setName("");
    setDescription("");
    setFormOpen(true);
  }

  function openEdit(item: SocialClient) {
    setMode("edit");
    setSelected(item);
    setName(item.name);
    setDescription(item.description ?? "");
    setFormOpen(true);
  }

  async function openDetails(item: SocialClient) {
    setDetails(item);
    await refreshDetails(item.id);
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    setSuccess(null);

    try {
      const payload = { name, description: description.trim() || null };
      const saved =
        mode === "create"
          ? await createSocialClient(payload, effectiveToken || undefined)
          : selected
            ? await updateSocialClient(selected.id, payload, effectiveToken || undefined)
            : null;

      setSuccess(mode === "create" ? "Client created." : "Client updated.");
      toast.success(mode === "create" ? "Client created" : "Client updated", name.trim());
      setFormOpen(false);
      setName("");
      setDescription("");
      if (saved) {
        setDetails(saved);
      }
      await loadClients();
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      pushErrorToast(message);
    } finally {
      setSubmitting(false);
    }
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
                Manage Social customers under the resolved organization.
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

      <div className="grid gap-3 md:grid-cols-4">
        <MetricCard icon={Users} label="Visible" value={stats.visible} hint="Rows in current view" />
        <MetricCard icon={CheckCircle2} label="Active" value={stats.active} hint="Ready for campaigns" />
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
                placeholder="Search clients by name..."
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
            <CardTitle className="flex items-center justify-between text-base">
              <span>Client grid</span>
              <span className="text-xs font-normal text-muted-foreground">
                Page {page?.page ?? pageNumber} of {Math.max(page?.totalPages ?? 1, 1)}
              </span>
            </CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            {loading ? (
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
                      <Fact label="Updated" value={formatDate(item.updatedAtUtc)} />
                      <Fact label="Created" value={formatDate(item.createdAtUtc)} />
                    </div>

                    <div className="mt-4 flex flex-wrap gap-2">
                      <Button variant="secondary" size="sm" onClick={() => openEdit(item)}>
                        <Edit3 className="h-4 w-4" /> Edit
                      </Button>
                      {item.isActive ? (
                        <Button
                          variant="destructive"
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
                    </div>
                  </motion.article>
                ))}
              </div>
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

        <ClientDetailsPanel
          client={details}
          onEdit={(item) => openEdit(item)}
          onArchive={(item) => void onArchive(item)}
          onReactivate={(item) => void onReactivate(item)}
          acting={actingId === details?.id}
        />
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Data table</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Description</TableHead>
                <TableHead>Created</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell className="font-medium">{item.name}</TableCell>
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
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      <Dialog open={formOpen} onOpenChange={setFormOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{mode === "create" ? "Create client" : "Edit client"}</DialogTitle>
            <DialogDescription>
              Manage the Social client profile for this organization.
            </DialogDescription>
          </DialogHeader>
          <form className="space-y-4" onSubmit={onSubmit}>
            <div className="space-y-2">
              <label htmlFor="client-name" className="text-sm font-medium">
                Name
              </label>
              <Input
                id="client-name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="e.g. Acme Growth Studio"
                required
              />
            </div>
            <div className="space-y-2">
              <label htmlFor="client-description" className="text-sm font-medium">
                Description <span className="text-muted-foreground">(optional)</span>
              </label>
              <textarea
                id="client-description"
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                placeholder="Brief description of this client..."
                rows={3}
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              />
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

function ClientDetailsPanel({
  client,
  onEdit,
  onArchive,
  onReactivate,
  acting
}: {
  client: SocialClient | null;
  onEdit: (client: SocialClient) => void;
  onArchive: (client: SocialClient) => void;
  onReactivate: (client: SocialClient) => void;
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
          {client.description ? <Fact label="Description" value={client.description} /> : null}
          <Fact label="Created" value={formatDateTime(client.createdAtUtc)} />
          <Fact label="Updated" value={formatDateTime(client.updatedAtUtc)} />
        </div>

        <ClientReferentAccountsSection clientId={client.id} />

        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => onEdit(client)}>
            <Edit3 className="h-4 w-4" /> Edit
          </Button>
          {client.isActive ? (
            <Button variant="destructive" disabled={acting} onClick={() => onArchive(client)}>
              <Archive className="h-4 w-4" /> Archive
            </Button>
          ) : (
            <Button variant="outline" disabled={acting} onClick={() => onReactivate(client)}>
              <RotateCcw className="h-4 w-4" /> Reactivate
            </Button>
          )}
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
        tone === "error" ? "border-red-500/40 bg-red-500/10 text-red-100" : "border-emerald-500/40 bg-emerald-500/10 text-emerald-100"
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
