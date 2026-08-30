"use client";

import { FormEvent, KeyboardEvent, useEffect, useRef, useState } from "react";
import {
  Archive,
  ChevronLeft,
  ChevronRight,
  Edit3,
  Hash,
  Plus,
  RefreshCcw,
  RotateCcw,
  Search,
  Sparkles,
  Tags,
  X
} from "lucide-react";
import {
  NICHE_DEFAULT_PAGE_SIZE,
  activateNiche,
  createNiche,
  deactivateNiche,
  listNiches,
  updateNiche
} from "@/lib/api/niches";
import { getNicheIcon, nicheIconOptions } from "@/lib/niche-icons";
import {
  MAX_NICHE_HASHTAGS,
  addNicheHashtag,
  splitNicheHashtagInput
} from "@/lib/social/niche-hashtags";
import { ApiError, Niche, PageResult } from "@/lib/api/types";
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
import { useDateFormatter } from "@/lib/format/use-date-formatter";

type Mode = "create" | "edit";
type StatusFilter = "active" | "archived" | "all";

// Code validation: lowercase alphanumeric + hyphens, 2-64 chars (mirrors the Niche domain rule)
const CODE_PATTERN = /^[a-z0-9-]{2,64}$/;
const DISPLAY_NAME_MAX_LENGTH = 120;

interface NichesAdminClientProps {
  isPlatformAdmin?: boolean;
}

export function NichesAdminClient({ isPlatformAdmin = false }: NichesAdminClientProps) {
  const sessionToken = useAccessToken();
  const toast = useToast();
  const { isInitialized: orgInitialized } = useActiveOrg();
  const { formatDate } = useDateFormatter();
  const lastErrorToastRef = useRef<{ message: string; at: number } | null>(null);

  const [page, setPage] = useState<PageResult<Niche> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState<StatusFilter>("active");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(NICHE_DEFAULT_PAGE_SIZE);
  const [formOpen, setFormOpen] = useState(false);
  const [mode, setMode] = useState<Mode>("create");
  const [selected, setSelected] = useState<Niche | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [actingId, setActingId] = useState<string | null>(null);

  // Form fields
  const [code, setCode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [iconKey, setIconKey] = useState<string>("");
  const [hashtags, setHashtags] = useState<string[]>([]);
  const [hashtagDraft, setHashtagDraft] = useState("");

  // Inline validation errors
  const [codeError, setCodeError] = useState<string | null>(null);
  const [displayNameError, setDisplayNameError] = useState<string | null>(null);
  const [hashtagError, setHashtagError] = useState<string | null>(null);

  const items = page?.items ?? [];
  const effectiveToken = sessionToken ?? "";

  function pushErrorToast(message: string) {
    const now = Date.now();
    const last = lastErrorToastRef.current;
    if (last && last.message === message && now - last.at < 2500) return;
    lastErrorToastRef.current = { message, at: now };
    toast.error("Niches error", message);
  }

  useEffect(() => {
    if (!orgInitialized) return;
    if (!effectiveToken) {
      setLoading(false);
      setPage(null);
      return;
    }
    void loadNiches();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageNumber, status, effectiveToken, orgInitialized]);

  async function loadNiches(nextPage = pageNumber) {
    setLoading(true);
    setError(null);
    try {
      const data = await listNiches(
        {
          // The API silently coerces this to false for non-admins, so asking is safe.
          includeInactive: status !== "active",
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

  function resetFormErrors() {
    setCodeError(null);
    setDisplayNameError(null);
    setHashtagError(null);
  }

  function openCreate() {
    setMode("create");
    setSelected(null);
    setCode("");
    setDisplayName("");
    setIconKey("");
    setHashtags([]);
    setHashtagDraft("");
    resetFormErrors();
    setFormOpen(true);
  }

  function openEdit(item: Niche) {
    setMode("edit");
    setSelected(item);
    setCode(item.code);
    setDisplayName(item.displayName);
    setIconKey(item.iconKey ?? "");
    setHashtags([...item.searchHashtags]);
    setHashtagDraft("");
    resetFormErrors();
    setFormOpen(true);
  }

  function commitHashtagDraft(raw: string) {
    const entries = splitNicheHashtagInput(raw);
    if (entries.length === 0) {
      setHashtagDraft("");
      return;
    }

    let next = hashtags;
    let firstError: string | null = null;
    for (const entry of entries) {
      const result = addNicheHashtag(next, entry);
      next = result.hashtags;
      if (result.error && !firstError) firstError = result.error;
    }

    setHashtags(next);
    setHashtagError(firstError);
    setHashtagDraft("");
  }

  function onHashtagKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter" || event.key === ",") {
      event.preventDefault();
      commitHashtagDraft(hashtagDraft);
      return;
    }

    if (event.key === "Backspace" && hashtagDraft.length === 0 && hashtags.length > 0) {
      event.preventDefault();
      setHashtags(hashtags.slice(0, -1));
      setHashtagError(null);
    }
  }

  function validateForm(): boolean {
    let valid = true;

    if (mode === "create") {
      if (!CODE_PATTERN.test(code.trim().toLowerCase())) {
        setCodeError("Code must be 2-64 chars: lowercase letters, digits, hyphens only.");
        valid = false;
      } else {
        setCodeError(null);
      }
    }

    if (!displayName.trim()) {
      setDisplayNameError("Display name is required.");
      valid = false;
    } else if (displayName.trim().length > DISPLAY_NAME_MAX_LENGTH) {
      setDisplayNameError(`Display name must not exceed ${DISPLAY_NAME_MAX_LENGTH} characters.`);
      valid = false;
    } else {
      setDisplayNameError(null);
    }

    return valid;
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    // An uncommitted draft is what the operator meant to add — take it before validating.
    const pendingDraft = hashtagDraft.trim();
    let effectiveHashtags = hashtags;
    if (pendingDraft.length > 0) {
      let next = hashtags;
      for (const entry of splitNicheHashtagInput(pendingDraft)) {
        next = addNicheHashtag(next, entry).hashtags;
      }
      effectiveHashtags = next;
      setHashtags(next);
      setHashtagDraft("");
    }

    if (!validateForm()) return;

    setSubmitting(true);
    setError(null);

    try {
      const normalizedIconKey = iconKey.trim() ? iconKey.trim() : null;

      if (mode === "create") {
        await createNiche(
          {
            code: code.trim().toLowerCase(),
            displayName: displayName.trim(),
            iconKey: normalizedIconKey,
            searchHashtags: effectiveHashtags
          },
          effectiveToken || undefined
        );
        toast.success("Niche created", displayName.trim());
      } else if (selected) {
        await updateNiche(
          selected.id,
          {
            displayName: displayName.trim(),
            iconKey: normalizedIconKey,
            searchHashtags: effectiveHashtags
          },
          effectiveToken || undefined
        );
        toast.success("Niche updated", displayName.trim());
      }

      setFormOpen(false);
      await loadNiches();
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      pushErrorToast(message);
    } finally {
      setSubmitting(false);
    }
  }

  async function onDeactivate(item: Niche) {
    await runAction(item.id, async () => {
      await deactivateNiche(item.id, effectiveToken || undefined);
      toast.success("Niche deactivated", item.displayName);
    });
  }

  async function onActivate(item: Niche) {
    await runAction(item.id, async () => {
      await activateNiche(item.id, effectiveToken || undefined);
      toast.success("Niche activated", item.displayName);
    });
  }

  async function runAction(id: string, action: () => Promise<void>) {
    setActingId(id);
    setError(null);
    try {
      await action();
      await loadNiches();
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
    void loadNiches(1);
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
              <Sparkles className="mr-1 h-3 w-3" /> Niches
            </Badge>
            <div>
              <h1 className="text-3xl font-semibold tracking-tight md:text-5xl">Niches catalog</h1>
              <p className="mt-3 text-sm leading-6 text-muted-foreground md:text-base">
                Platform-managed catalog of sectors and niches. Clients pick from these entries instead
                of typing free text, and the curated hashtags below are what discovery searches on.
                PlatformAdmin only for writes.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button variant="outline" onClick={() => void loadNiches()}>
              <RefreshCcw className="h-4 w-4" /> Refresh
            </Button>
            {isPlatformAdmin && (
              <Button onClick={openCreate}>
                <Plus className="h-4 w-4" /> New niche
              </Button>
            )}
          </div>
        </div>
      </section>

      {/* Filters */}
      <Card>
        <CardContent className="p-4">
          <form className="grid gap-3 lg:grid-cols-[1fr_auto_auto]" onSubmit={applyFilters}>
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
            <Button type="submit">Apply</Button>
          </form>
          <p className="mt-2 text-xs text-muted-foreground">
            With no search term the catalog returns the most recently created entries first, capped at{" "}
            {pageSize} per page. Search to find older entries.
          </p>
        </CardContent>
      </Card>

      {error ? <Notice tone="error" message={error} onDismiss={() => setError(null)} /> : null}

      {/* Table */}
      <Card className="overflow-hidden">
        <CardHeader className="border-b border-border">
          <CardTitle className="flex items-center justify-between text-base">
            <span>Niches</span>
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
                  <TableHead>Curated hashtags</TableHead>
                  <TableHead>Created</TableHead>
                  <TableHead>Status</TableHead>
                  {isPlatformAdmin && <TableHead className="text-right">Actions</TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((item) => {
                  const NicheIcon = getNicheIcon(item.iconKey);
                  return (
                    <TableRow key={item.id}>
                      <TableCell>
                        <div
                          className="flex h-9 w-9 items-center justify-center rounded-md border border-border bg-muted/40"
                          title={item.iconKey ?? "No icon — generic fallback"}
                        >
                          <NicheIcon className="h-5 w-5 text-muted-foreground" />
                        </div>
                      </TableCell>
                      <TableCell className="font-mono text-sm">{item.code}</TableCell>
                      <TableCell className="font-medium">{item.displayName}</TableCell>
                      <TableCell>
                        {item.searchHashtags.length === 0 ? (
                          <span className="text-xs text-muted-foreground">None curated</span>
                        ) : (
                          <div className="flex flex-wrap gap-1">
                            {item.searchHashtags.map((tag) => (
                              <Badge key={tag} variant="muted" className="font-mono">
                                #{tag}
                              </Badge>
                            ))}
                          </div>
                        )}
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {formatDate(item.createdAtUtc)}
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
            <span className="text-xs text-muted-foreground">{pageSize} per page</span>
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
            <DialogTitle>{mode === "create" ? "Create niche" : "Edit niche"}</DialogTitle>
            <DialogDescription>
              {mode === "create"
                ? "Add a new sector/niche to the platform catalog."
                : "Update display name, icon and curated hashtags. Code is immutable after creation."}
            </DialogDescription>
          </DialogHeader>
          <form className="max-h-[70vh] space-y-4 overflow-y-auto pr-1" onSubmit={onSubmit}>
            {/* Code field — disabled in edit mode, mirroring the network catalog */}
            <div className="space-y-1">
              <label htmlFor="niche-code" className="text-sm font-medium">
                Code
              </label>
              <Input
                id="niche-code"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                placeholder="e.g. gastronomy"
                disabled={mode === "edit"}
                title={mode === "edit" ? "Code is immutable — deactivate and recreate to change." : undefined}
                className={cn(mode === "edit" && "cursor-not-allowed opacity-60")}
              />
              {mode === "edit" && (
                <p className="text-xs text-muted-foreground">
                  Code is immutable — client associations point at it.
                </p>
              )}
              {codeError && <p className="text-xs text-red-500">{codeError}</p>}
            </div>

            {/* Display Name */}
            <div className="space-y-1">
              <label htmlFor="niche-display-name" className="text-sm font-medium">
                Display Name
              </label>
              <Input
                id="niche-display-name"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                placeholder="e.g. Gastronomy"
                maxLength={DISPLAY_NAME_MAX_LENGTH}
              />
              {displayNameError && <p className="text-xs text-red-500">{displayNameError}</p>}
            </div>

            {/* Curated hashtags — the reason this catalog improves discovery */}
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <label htmlFor="niche-hashtag-input" className="text-sm font-medium">
                  Curated search hashtags
                </label>
                <span className="text-xs text-muted-foreground">
                  {hashtags.length}/{MAX_NICHE_HASHTAGS}
                </span>
              </div>
              <p className="text-xs text-muted-foreground">
                These drive discovery for every client on this niche. Stored, not derived from the name —
                so a tag nobody uses can be fixed once, for everyone. Press Enter or comma to add.
              </p>
              {hashtags.length > 0 && (
                <ul className="flex flex-wrap gap-1.5">
                  {hashtags.map((tag) => (
                    <li key={tag}>
                      <span className="inline-flex items-center gap-1.5 rounded-full border border-border bg-muted/60 py-1 pl-2.5 pr-1 font-mono text-xs">
                        #{tag}
                        <button
                          type="button"
                          aria-label={`Remove hashtag ${tag}`}
                          onClick={() => {
                            setHashtags(hashtags.filter((x) => x !== tag));
                            setHashtagError(null);
                          }}
                          className="rounded-full p-0.5 text-muted-foreground transition hover:bg-background hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                        >
                          <X className="h-3 w-3" />
                        </button>
                      </span>
                    </li>
                  ))}
                </ul>
              )}
              <div className="relative">
                <Hash className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
                <Input
                  id="niche-hashtag-input"
                  className="pl-9"
                  value={hashtagDraft}
                  onChange={(e) => {
                    setHashtagDraft(e.target.value);
                    setHashtagError(null);
                  }}
                  onKeyDown={onHashtagKeyDown}
                  onBlur={() => commitHashtagDraft(hashtagDraft)}
                  placeholder="e.g. gastronomy, food"
                  disabled={hashtags.length >= MAX_NICHE_HASHTAGS}
                />
              </div>
              {hashtagError && <p className="text-xs text-red-500">{hashtagError}</p>}
            </div>

            {/* Icon — optional; no selection means the generic fallback icon */}
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <label className="text-sm font-medium">
                  Icon <span className="text-xs text-muted-foreground">(optional)</span>
                </label>
                {iconKey ? <span className="font-mono text-xs text-muted-foreground">{iconKey}</span> : null}
              </div>
              <div className="grid max-h-64 grid-cols-2 gap-2 overflow-y-auto rounded-xl border border-border bg-background/40 p-2 sm:grid-cols-3">
                <NoIconOption selected={iconKey === ""} onSelect={() => setIconKey("")} />
                {nicheIconOptions.map((option) => {
                  const Icon = option.icon;
                  const isSelected = iconKey === option.key;

                  return (
                    <button
                      key={option.key}
                      type="button"
                      aria-pressed={isSelected}
                      onClick={() => setIconKey(option.key)}
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

function NoIconOption({ selected, onSelect }: { selected: boolean; onSelect: () => void }) {
  const FallbackIcon = getNicheIcon(null);
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onSelect}
      className={cn(
        "flex min-h-16 items-center gap-2 rounded-md border p-2 text-left text-sm transition",
        "hover:border-primary/60 hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary",
        selected ? "border-primary bg-primary/10 text-foreground" : "border-border bg-card/50 text-muted-foreground"
      )}
    >
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md border border-dashed border-border bg-background">
        <FallbackIcon className="h-5 w-5" />
      </span>
      <span className="min-w-0">
        <span className="block truncate font-medium">No icon</span>
        <span className="block truncate text-xs opacity-75">Generic fallback</span>
      </span>
    </button>
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
        <Tags className="h-8 w-8 text-primary" />
      </div>
      <h2 className="text-lg font-semibold">No niches in this view</h2>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">
        The platform niche catalog is empty or no entry matches the current filter.
      </p>
      {canCreate && (
        <Button className="mt-5" onClick={onCreate}>
          <Plus className="h-4 w-4" /> Create niche
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
