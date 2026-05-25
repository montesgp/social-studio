"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import {
  Archive,
  ChevronLeft,
  ChevronRight,
  Edit3,
  Plus,
  RefreshCcw,
  RotateCcw,
  Search,
  Share2,
  Sparkles,
  X
} from "lucide-react";
import {
  activateSocialNetwork,
  createSocialNetwork,
  deactivateSocialNetwork,
  listSocialNetworks,
  updateSocialNetwork
} from "@/lib/api/social-networks";
import {
  getSocialNetworkIcon,
  getSocialNetworkIconOption,
  socialNetworkIconOptions
} from "@/lib/social-network-icons";
import { ApiError, PageResult, SocialNetwork } from "@/lib/api/types";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useToast } from "@/components/ui/toaster";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";

type Mode = "create" | "edit";
type StatusFilter = "active" | "archived" | "all";

// Code validation: lowercase alphanumeric + hyphens, 2-32 chars (mirrors domain rule)
const CODE_PATTERN = /^[a-z0-9-]{2,32}$/;

interface NetworksAdminClientProps {
  isPlatformAdmin?: boolean;
}

export function NetworksAdminClient({ isPlatformAdmin = false }: NetworksAdminClientProps) {
  const sessionToken = useAccessToken();
  const toast = useToast();
  const { isInitialized: orgInitialized } = useActiveOrg();
  const lastErrorToastRef = useRef<{ message: string; at: number } | null>(null);

  const [page, setPage] = useState<PageResult<SocialNetwork> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState<StatusFilter>("active");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(20);
  const [formOpen, setFormOpen] = useState(false);
  const [mode, setMode] = useState<Mode>("create");
  const [selected, setSelected] = useState<SocialNetwork | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [actingId, setActingId] = useState<string | null>(null);

  // Form fields
  const [code, setCode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [iconKey, setIconKey] = useState("");

  // Inline validation errors
  const [codeError, setCodeError] = useState<string | null>(null);
  const [displayNameError, setDisplayNameError] = useState<string | null>(null);
  const [iconKeyError, setIconKeyError] = useState<string | null>(null);

  const items = page?.items ?? [];
  const effectiveToken = sessionToken ?? "";

  function pushErrorToast(message: string) {
    const now = Date.now();
    const last = lastErrorToastRef.current;
    if (last && last.message === message && now - last.at < 2500) return;
    lastErrorToastRef.current = { message, at: now };
    toast.error("Social Networks error", message);
  }

  useEffect(() => {
    if (!orgInitialized) return;
    if (!effectiveToken) {
      setLoading(false);
      setPage(null);
      return;
    }
    void loadNetworks();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageNumber, status, includeInactive, effectiveToken, orgInitialized]);

  async function loadNetworks(nextPage = pageNumber) {
    setLoading(true);
    setError(null);
    try {
      const data = await listSocialNetworks(
        {
          includeInactive: status !== "active" || includeInactive,
          page: nextPage,
          pageSize,
          searchTerm: query || undefined
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

  function openCreate() {
    setMode("create");
    setSelected(null);
    setCode("");
    setDisplayName("");
    setIconKey("");
    setCodeError(null);
    setDisplayNameError(null);
    setIconKeyError(null);
    setFormOpen(true);
  }

  function openEdit(item: SocialNetwork) {
    setMode("edit");
    setSelected(item);
    setCode(item.code);
    setDisplayName(item.displayName);
    setIconKey(item.iconKey);
    setCodeError(null);
    setDisplayNameError(null);
    setIconKeyError(null);
    setFormOpen(true);
  }

  function validateForm(): boolean {
    let valid = true;

    if (mode === "create") {
      if (!CODE_PATTERN.test(code)) {
        setCodeError("Code must be 2-32 chars: lowercase letters, digits, hyphens only.");
        valid = false;
      } else {
        setCodeError(null);
      }
    }

    if (!displayName.trim()) {
      setDisplayNameError("Display name is required.");
      valid = false;
    } else if (displayName.trim().length > 80) {
      setDisplayNameError("Display name must not exceed 80 characters.");
      valid = false;
    } else {
      setDisplayNameError(null);
    }

    const normalizedIconKey = iconKey.trim();
    const isKnownIcon = socialNetworkIconOptions.some((option) => option.key === normalizedIconKey);

    if (!normalizedIconKey) {
      setIconKeyError("Select an icon for this network.");
      valid = false;
    } else if (!isKnownIcon) {
      setIconKeyError("Select one of the available social network icons.");
      valid = false;
    } else {
      setIconKeyError(null);
    }

    return valid;
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!validateForm()) return;

    setSubmitting(true);
    setError(null);

    try {
      if (mode === "create") {
        await createSocialNetwork(
          { code: code.trim(), displayName: displayName.trim(), iconKey: iconKey.trim() },
          effectiveToken || undefined
        );
        toast.success("Network created", displayName.trim());
      } else if (selected) {
        await updateSocialNetwork(
          selected.id,
          { displayName: displayName.trim(), iconKey: iconKey.trim() },
          effectiveToken || undefined
        );
        toast.success("Network updated", displayName.trim());
      }

      setFormOpen(false);
      await loadNetworks();
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      pushErrorToast(message);
    } finally {
      setSubmitting(false);
    }
  }

  async function onDeactivate(item: SocialNetwork) {
    await runAction(item.id, async () => {
      await deactivateSocialNetwork(item.id, effectiveToken || undefined);
      toast.success("Network deactivated", item.displayName);
    });
  }

  async function onActivate(item: SocialNetwork) {
    await runAction(item.id, async () => {
      await activateSocialNetwork(item.id, effectiveToken || undefined);
      toast.success("Network activated", item.displayName);
    });
  }

  async function runAction(id: string, action: () => Promise<void>) {
    setActingId(id);
    setError(null);
    try {
      await action();
      await loadNetworks();
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
    void loadNetworks(1);
  }

  return (
    <div className="space-y-6">
      {/* Hero header */}
      <section className="relative overflow-hidden rounded-3xl border border-border bg-card p-6 shadow-2xl">
        <div className="absolute -right-20 -top-20 h-56 w-56 rounded-full bg-primary/20 blur-3xl" />
        <div className="absolute -bottom-24 left-1/3 h-56 w-56 rounded-full bg-blue-400/10 blur-3xl" />
        <div className="relative flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
          <div className="max-w-3xl space-y-3">
            <Badge variant="success" className="w-fit">
              <Sparkles className="mr-1 h-3 w-3" /> Social Networks
            </Badge>
            <div>
              <h1 className="text-3xl font-semibold tracking-tight md:text-5xl">Networks catalog</h1>
              <p className="mt-3 text-sm leading-6 text-muted-foreground md:text-base">
                Platform-managed catalog of social networks. PlatformAdmin only for writes.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button variant="outline" onClick={() => void loadNetworks()}>
              <RefreshCcw className="h-4 w-4" /> Refresh
            </Button>
            {isPlatformAdmin && (
              <Button onClick={openCreate}>
                <Plus className="h-4 w-4" /> New network
              </Button>
            )}
          </div>
        </div>
      </section>

      {/* Filters */}
      <Card>
        <CardContent className="p-4">
          <form
            className={cn("grid gap-3", isPlatformAdmin ? "lg:grid-cols-[1fr_auto_auto_auto]" : "lg:grid-cols-[1fr_auto_auto]")}
            onSubmit={applyFilters}
          >
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
              <Input
                className="pl-9"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Search by name or code..."
              />
            </div>
            <StatusTabs value={status} onChange={setStatus} />
            {isPlatformAdmin && (
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={includeInactive}
                  onChange={(e) => setIncludeInactive(e.target.checked)}
                  className="rounded border-border"
                />
                Include inactive
              </label>
            )}
            <Button type="submit">Apply</Button>
          </form>
        </CardContent>
      </Card>

      {error ? <Notice tone="error" message={error} onDismiss={() => setError(null)} /> : null}

      {/* Table */}
      <Card className="overflow-hidden">
        <CardHeader className="border-b border-border">
          <CardTitle className="flex items-center justify-between text-base">
            <span>Networks</span>
            <span className="text-xs font-normal text-muted-foreground">
              {page?.totalCount ?? 0} total • Page {page?.page ?? pageNumber} of {Math.max(page?.totalPages ?? 1, 1)}
            </span>
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {loading ? (
            <div className="space-y-2 p-4">
              {Array.from({ length: 3 }).map((_, i) => (
                <div key={i} className="h-12 animate-pulse rounded-xl border border-border bg-muted/40" />
              ))}
            </div>
          ) : items.length === 0 ? (
            <EmptyState onCreate={openCreate} canCreate={isPlatformAdmin} />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Icon</TableHead>
                  <TableHead>Code</TableHead>
                  <TableHead>Display Name</TableHead>
                  <TableHead>Icon Key</TableHead>
                  <TableHead>Status</TableHead>
                  {isPlatformAdmin && <TableHead className="text-right">Actions</TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((item) => {
                  const NetworkIcon = getSocialNetworkIcon(item.iconKey);
                  const iconOption = getSocialNetworkIconOption(item.iconKey);
                  return (
                    <TableRow key={item.id}>
                      <TableCell>
                        <div className="flex h-9 w-9 items-center justify-center rounded-md border border-border bg-muted/40">
                          <NetworkIcon className="h-5 w-5 text-muted-foreground" />
                        </div>
                      </TableCell>
                      <TableCell className="font-mono text-sm">{item.code}</TableCell>
                      <TableCell className="font-medium">{item.displayName}</TableCell>
                      <TableCell>
                        <div className="space-y-0.5">
                          <p className="text-sm">{iconOption?.label ?? "Unknown icon"}</p>
                          <p className="font-mono text-xs text-muted-foreground">{item.iconKey}</p>
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge variant={item.isActive ? "success" : "muted"}>
                          {item.isActive ? "Active" : "Inactive"}
                        </Badge>
                      </TableCell>
                      {isPlatformAdmin && (
                        <TableCell className="text-right">
                          <div className="inline-flex gap-2">
                            <Button variant="secondary" size="sm" onClick={() => openEdit(item)}>
                              <Edit3 className="h-4 w-4" /> Edit
                            </Button>
                            {item.isActive ? (
                              <Button
                                variant="destructive"
                                size="sm"
                                disabled={actingId === item.id}
                                onClick={() => void onDeactivate(item)}
                              >
                                <Archive className="h-4 w-4" /> Deactivate
                              </Button>
                            ) : (
                              <Button
                                variant="outline"
                                size="sm"
                                disabled={actingId === item.id}
                                onClick={() => void onActivate(item)}
                              >
                                <RotateCcw className="h-4 w-4" /> Activate
                              </Button>
                            )}
                          </div>
                        </TableCell>
                      )}
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}

          <div className="flex items-center justify-between border-t border-border p-4">
            <Button
              variant="outline"
              disabled={!page?.hasPreviousPage || loading}
              onClick={() => setPageNumber((v) => Math.max(1, v - 1))}
            >
              <ChevronLeft className="h-4 w-4" /> Previous
            </Button>
            <span className="text-xs text-muted-foreground">
              {pageSize} per page
            </span>
            <Button
              variant="outline"
              disabled={!page?.hasNextPage || loading}
              onClick={() => setPageNumber((v) => v + 1)}
            >
              Next <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </CardContent>
      </Card>

      {/* Create / Edit Dialog */}
      <Dialog open={formOpen} onOpenChange={setFormOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{mode === "create" ? "Create social network" : "Edit social network"}</DialogTitle>
            <DialogDescription>
              {mode === "create"
                ? "Add a new social network to the platform catalog."
                : "Update display name and icon key. Code is immutable after creation."}
            </DialogDescription>
          </DialogHeader>
          <form className="space-y-4" onSubmit={onSubmit}>
            {/* Code field — disabled in edit mode (ADR 6) */}
            <div className="space-y-1">
              <label htmlFor="network-code" className="text-sm font-medium">
                Code
              </label>
              <Input
                id="network-code"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                placeholder="e.g. instagram"
                disabled={mode === "edit"}
                title={mode === "edit" ? "Code is immutable — deactivate and recreate to change." : undefined}
                className={cn(mode === "edit" && "cursor-not-allowed opacity-60")}
              />
              {mode === "edit" && (
                <p className="text-xs text-muted-foreground">Code is immutable — deactivate and recreate to change.</p>
              )}
              {codeError && <p className="text-xs text-red-500">{codeError}</p>}
            </div>

            {/* Display Name */}
            <div className="space-y-1">
              <label htmlFor="network-display-name" className="text-sm font-medium">
                Display Name
              </label>
              <Input
                id="network-display-name"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                placeholder="e.g. Instagram"
                maxLength={80}
              />
              {displayNameError && <p className="text-xs text-red-500">{displayNameError}</p>}
            </div>

            <div className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <label className="text-sm font-medium">Icon</label>
                {iconKey ? <span className="font-mono text-xs text-muted-foreground">{iconKey}</span> : null}
              </div>
              <div className="grid max-h-72 grid-cols-2 gap-2 overflow-y-auto rounded-xl border border-border bg-background/40 p-2 sm:grid-cols-3">
                {socialNetworkIconOptions.map((option) => {
                  const Icon = option.icon;
                  const isSelected = iconKey === option.key;

                  return (
                    <button
                      key={option.key}
                      type="button"
                      aria-pressed={isSelected}
                      onClick={() => {
                        setIconKey(option.key);
                        setIconKeyError(null);
                      }}
                      className={cn(
                        "flex min-h-16 items-center gap-2 rounded-md border p-2 text-left text-sm transition",
                        "hover:border-primary/60 hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary",
                        isSelected
                          ? "border-primary bg-primary/10 text-foreground"
                          : "border-border bg-card/50 text-muted-foreground"
                      )}
                    >
                      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md border border-border bg-background">
                        <Icon className="h-5 w-5" />
                      </span>
                      <span className="min-w-0">
                        <span className="block truncate font-medium">{option.label}</span>
                        <span className="block truncate font-mono text-xs opacity-75">{option.key}</span>
                      </span>
                    </button>
                  );
                })}
              </div>
              {iconKeyError && <p className="text-xs text-red-500">{iconKeyError}</p>}
            </div>

            <div className="flex justify-end gap-2">
              <Button type="button" variant="secondary" onClick={() => setFormOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? "Saving..." : mode === "create" ? "Create" : "Save changes"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function StatusTabs({ value, onChange }: { value: StatusFilter; onChange: (v: StatusFilter) => void }) {
  const options: Array<{ value: StatusFilter; label: string }> = [
    { value: "active", label: "Active" },
    { value: "archived", label: "Inactive" },
    { value: "all", label: "All" }
  ];
  return (
    <div className="inline-flex rounded-md border border-border bg-background p-1">
      {options.map((opt) => (
        <button
          key={opt.value}
          type="button"
          onClick={() => onChange(opt.value)}
          className={cn(
            "rounded px-3 py-2 text-sm text-muted-foreground transition",
            value === opt.value && "bg-muted text-foreground"
          )}
        >
          {opt.label}
        </button>
      ))}
    </div>
  );
}

function EmptyState({ onCreate, canCreate }: { onCreate: () => void; canCreate: boolean }) {
  return (
    <div className="flex min-h-64 flex-col items-center justify-center p-8 text-center">
      <div className="mb-4 rounded-3xl border border-border bg-muted p-5">
        <Share2 className="h-8 w-8 text-primary" />
      </div>
      <h2 className="text-lg font-semibold">No networks in this view</h2>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">
        The platform social network catalog is empty or all networks match the current filter.
      </p>
      {canCreate && (
        <Button className="mt-5" onClick={onCreate}>
          <Plus className="h-4 w-4" /> Create network
        </Button>
      )}
    </div>
  );
}

function Notice({ tone, message, onDismiss }: { tone: "error" | "success"; message: string; onDismiss: () => void }) {
  return (
    <div
      className={cn(
        "flex items-center justify-between rounded-xl border p-4 text-sm",
        tone === "error"
          ? "border-red-500/40 bg-red-500/10 text-red-100"
          : "border-emerald-500/40 bg-emerald-500/10 text-emerald-100"
      )}
    >
      <span>{tone === "error" ? `Error: ${message}` : message}</span>
      <button type="button" onClick={onDismiss} className="opacity-70 hover:opacity-100">
        <X className="h-4 w-4" />
      </button>
    </div>
  );
}
