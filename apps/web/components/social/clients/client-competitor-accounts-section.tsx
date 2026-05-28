"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import { ExternalLink, Plus } from "lucide-react";
import {
  activateClientCompetitorAccount,
  createClientCompetitorAccount,
  deactivateClientCompetitorAccount,
  listClientCompetitorAccounts,
  reorderClientCompetitorAccounts,
  updateClientCompetitorAccount,
} from "@/lib/api/client-competitor-accounts";
import { listSocialNetworks } from "@/lib/api/social-networks";
import type {
  ClientCompetitorAccount,
  CreateClientCompetitorAccountPayload,
  SocialNetwork,
  UpdateClientCompetitorAccountPayload,
} from "@/lib/api/types";
import { getSocialNetworkIcon } from "@/lib/social-network-icons";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { SortableChildTable, Column } from "@/components/ui/sortable-child-table";
import { useToast } from "@/components/ui/toaster";
import { useAccessToken } from "@/components/auth/use-access-token";

interface ClientCompetitorAccountsSectionProps {
  clientId: string;
  accounts?: ClientCompetitorAccount[];
  loading?: boolean;
  deferPersistence?: boolean;
  onAccountsChange?: (accounts: ClientCompetitorAccount[]) => void;
}

function truncate(str: string, max: number): string {
  return str.length <= max ? str : `${str.slice(0, max)}...`;
}

function validateProfileUrl(url: string): string | null {
  if (!url.trim()) return "Profile URL is required.";
  if (!url.startsWith("http://") && !url.startsWith("https://")) {
    return "Profile URL must start with http:// or https://";
  }
  if (url.length < 10) return "Profile URL is too short (minimum 10 characters).";
  if (url.length > 2048) return "Profile URL is too long (maximum 2048 characters).";
  return null;
}

function createDraftId() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return `draft-${crypto.randomUUID()}`;
  }

  return `draft-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function nowIso() {
  return new Date().toISOString();
}

function applySortOrder(accounts: ClientCompetitorAccount[]) {
  return accounts.map((account, index) => ({ ...account, sortOrder: index, updatedAtUtc: nowIso() }));
}

export function ClientCompetitorAccountsSection({
  clientId,
  accounts,
  loading,
  deferPersistence = false,
  onAccountsChange,
}: ClientCompetitorAccountsSectionProps) {
  const token = useAccessToken();
  const toast = useToast();

  const [persistedAccounts, setPersistedAccounts] = useState<ClientCompetitorAccount[]>([]);
  const [networks, setNetworks] = useState<SocialNetwork[]>([]);
  const [loadingAccounts, setLoadingAccounts] = useState(true);
  const [showArchived, setShowArchived] = useState(false);

  const [createOpen, setCreateOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [editing, setEditing] = useState<ClientCompetitorAccount | null>(null);

  const [createNetworkId, setCreateNetworkId] = useState("");
  const [createProfileUrl, setCreateProfileUrl] = useState("");
  const [createHandle, setCreateHandle] = useState("");
  const [createLabel, setCreateLabel] = useState("");
  const [createNotes, setCreateNotes] = useState("");
  const [createNetworkError, setCreateNetworkError] = useState<string | null>(null);
  const [createProfileUrlError, setCreateProfileUrlError] = useState<string | null>(null);
  const [submittingCreate, setSubmittingCreate] = useState(false);

  const [editNetworkId, setEditNetworkId] = useState("");
  const [editProfileUrl, setEditProfileUrl] = useState("");
  const [editHandle, setEditHandle] = useState("");
  const [editLabel, setEditLabel] = useState("");
  const [editNotes, setEditNotes] = useState("");
  const [editNetworkError, setEditNetworkError] = useState<string | null>(null);
  const [editProfileUrlError, setEditProfileUrlError] = useState<string | null>(null);
  const [submittingEdit, setSubmittingEdit] = useState(false);

  const effectiveToken = token ?? undefined;
  const draftMode = deferPersistence && Boolean(accounts && onAccountsChange);
  const allAccounts = useMemo(
    () => (draftMode ? accounts ?? [] : persistedAccounts),
    [accounts, draftMode, persistedAccounts],
  );
  const visibleAccounts = useMemo(
    () => (showArchived ? allAccounts : allAccounts.filter((account) => account.isActive)),
    [allAccounts, showArchived],
  );

  const updateAccounts = useCallback(
    (next: ClientCompetitorAccount[]) => {
      const sorted = [...next].sort((a, b) => a.sortOrder - b.sortOrder);
      if (draftMode) {
        onAccountsChange?.(sorted);
      } else {
        setPersistedAccounts(sorted);
      }
    },
    [draftMode, onAccountsChange],
  );

  const loadAccounts = useCallback(async () => {
    if (!effectiveToken || draftMode) return;
    setLoadingAccounts(true);
    try {
      const result = await listClientCompetitorAccounts(
        clientId,
        { includeInactive: true, page: 1, pageSize: 200 },
        effectiveToken,
      );
      setPersistedAccounts([...result.items].sort((a, b) => a.sortOrder - b.sortOrder));
    } catch {
      toast.error("Competitor accounts", "Failed to load competitor accounts.");
    } finally {
      setLoadingAccounts(false);
    }
  }, [clientId, draftMode, effectiveToken, toast]);

  useEffect(() => {
    void loadAccounts();
  }, [loadAccounts]);

  useEffect(() => {
    if (!effectiveToken) return;
    listSocialNetworks({}, effectiveToken)
      .then((result) => setNetworks(result.items))
      .catch(() => {});
  }, [effectiveToken]);

  function findNetwork(id: string) {
    return networks.find((network) => network.id === id) ?? null;
  }

  function validateNetwork(id: string): string | null {
    return findNetwork(id) ? null : "Social Network is required.";
  }

  function validateDuplicate(networkId: string, profileUrl: string, excludeId?: string): string | null {
    const normalizedUrl = profileUrl.trim();
    const duplicate = allAccounts.some(
      (account) =>
        account.id !== excludeId &&
        account.socialNetworkId === networkId &&
        account.profileUrl.trim() === normalizedUrl,
    );

    return duplicate ? "This profile URL already exists for the selected social network." : null;
  }

  function openCreate() {
    setCreateNetworkId(networks[0]?.id ?? "");
    setCreateProfileUrl("");
    setCreateHandle("");
    setCreateLabel("");
    setCreateNotes("");
    setCreateNetworkError(null);
    setCreateProfileUrlError(null);
    setCreateOpen(true);
  }

  function openEdit(account: ClientCompetitorAccount) {
    setEditing(account);
    setEditNetworkId(account.socialNetworkId);
    setEditProfileUrl(account.profileUrl);
    setEditHandle(account.handle ?? "");
    setEditLabel(account.label ?? "");
    setEditNotes(account.notes ?? "");
    setEditNetworkError(null);
    setEditProfileUrlError(null);
    setEditOpen(true);
  }

  async function handleCreate(e: FormEvent) {
    e.preventDefault();
    e.stopPropagation();
    const networkError = validateNetwork(createNetworkId);
    const urlError =
      validateProfileUrl(createProfileUrl) ??
      validateDuplicate(createNetworkId, createProfileUrl);

    setCreateNetworkError(networkError);
    setCreateProfileUrlError(urlError);
    if (networkError || urlError) return;

    const network = findNetwork(createNetworkId);
    if (!network) return;

    if (draftMode) {
      const nextSortOrder = allAccounts.length === 0 ? 0 : Math.max(...allAccounts.map((x) => x.sortOrder)) + 1;
      const created: ClientCompetitorAccount = {
        id: createDraftId(),
        clientId,
        organizationId: allAccounts[0]?.organizationId ?? "",
        socialNetworkId: network.id,
        socialNetworkCode: network.code,
        socialNetworkDisplayName: network.displayName,
        socialNetworkIconKey: network.iconKey,
        profileUrl: createProfileUrl.trim(),
        handle: createHandle.trim() || null,
        label: createLabel.trim() || null,
        notes: createNotes.trim() || null,
        isActive: true,
        sortOrder: nextSortOrder,
        createdAtUtc: nowIso(),
        updatedAtUtc: nowIso(),
      };
      updateAccounts([...allAccounts, created]);
      setCreateOpen(false);
      return;
    }

    setSubmittingCreate(true);
    try {
      const payload: CreateClientCompetitorAccountPayload = {
        socialNetworkId: createNetworkId,
        profileUrl: createProfileUrl.trim(),
        handle: createHandle.trim() || undefined,
        label: createLabel.trim() || undefined,
        notes: createNotes.trim() || undefined,
      };
      await createClientCompetitorAccount(clientId, payload, effectiveToken);
      toast.success("Competitor account created", "The competitor account has been added.");
      setCreateOpen(false);
      void loadAccounts();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to create competitor account.";
      toast.error("Create failed", message);
    } finally {
      setSubmittingCreate(false);
    }
  }

  async function handleUpdate(e: FormEvent) {
    e.preventDefault();
    e.stopPropagation();
    if (!editing) return;

    const networkError = validateNetwork(editNetworkId);
    const urlError =
      validateProfileUrl(editProfileUrl) ??
      validateDuplicate(editNetworkId, editProfileUrl, editing.id);

    setEditNetworkError(networkError);
    setEditProfileUrlError(urlError);
    if (networkError || urlError) return;

    const network = findNetwork(editNetworkId);
    if (!network) return;

    if (draftMode) {
      updateAccounts(
        allAccounts.map((account) =>
          account.id === editing.id
            ? {
                ...account,
                socialNetworkId: network.id,
                socialNetworkCode: network.code,
                socialNetworkDisplayName: network.displayName,
                socialNetworkIconKey: network.iconKey,
                profileUrl: editProfileUrl.trim(),
                handle: editHandle.trim() || null,
                label: editLabel.trim() || null,
                notes: editNotes.trim() || null,
                updatedAtUtc: nowIso(),
              }
            : account,
        ),
      );
      setEditOpen(false);
      return;
    }

    setSubmittingEdit(true);
    try {
      const payload: UpdateClientCompetitorAccountPayload = {
        socialNetworkId: editNetworkId,
        profileUrl: editProfileUrl.trim(),
        handle: editHandle.trim() || undefined,
        label: editLabel.trim() || undefined,
        notes: editNotes.trim() || undefined,
      };
      await updateClientCompetitorAccount(clientId, editing.id, payload, effectiveToken);
      toast.success("Competitor account updated", "Changes saved successfully.");
      setEditOpen(false);
      void loadAccounts();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to update competitor account.";
      toast.error("Update failed", message);
    } finally {
      setSubmittingEdit(false);
    }
  }

  async function handleReorder(orderedIds: string[]) {
    const orderedVisible = orderedIds
      .map((id) => allAccounts.find((account) => account.id === id))
      .filter((account): account is ClientCompetitorAccount => Boolean(account));
    const hidden = allAccounts.filter((account) => !orderedIds.includes(account.id));
    const next = applySortOrder([...orderedVisible, ...hidden]);

    if (draftMode) {
      updateAccounts(next);
      return;
    }

    updateAccounts(next);
    try {
      await reorderClientCompetitorAccounts(clientId, next.map((account) => account.id), effectiveToken);
      void loadAccounts();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to reorder accounts.";
      toast.error("Reorder failed", message);
      void loadAccounts();
    }
  }

  async function handleToggleActive(id: string, currentlyActive: boolean) {
    if (draftMode) {
      updateAccounts(
        allAccounts.map((account) =>
          account.id === id
            ? { ...account, isActive: !currentlyActive, updatedAtUtc: nowIso() }
            : account,
        ),
      );
      return;
    }

    try {
      if (currentlyActive) {
        await deactivateClientCompetitorAccount(clientId, id, effectiveToken);
        toast.success("Deactivated", "Competitor account archived.");
      } else {
        await activateClientCompetitorAccount(clientId, id, effectiveToken);
        toast.success("Activated", "Competitor account reactivated.");
      }
      void loadAccounts();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to update account status.";
      toast.error("Status update failed", message);
    }
  }

  const columns: Column<ClientCompetitorAccount>[] = [
    {
      key: "account",
      label: "Account",
      render: (account) => {
        const Icon = getSocialNetworkIcon(account.socialNetworkIconKey);
        return (
          <div className="flex items-center gap-2 min-w-0">
            <Icon className="h-4 w-4 shrink-0 text-muted-foreground" />
            <div className="min-w-0">
              <p className="truncate text-sm font-medium">
                {account.handle ?? truncate(account.profileUrl, 40)}
              </p>
              {account.label ? (
                <p className="text-xs text-muted-foreground">{account.label}</p>
              ) : null}
            </div>
          </div>
        );
      },
    },
    {
      key: "network",
      label: "Network",
      render: (account) => (
        <span className="text-xs text-muted-foreground">{account.socialNetworkDisplayName}</span>
      ),
    },
    {
      key: "url",
      label: "URL",
      render: (account) => (
        <a
          href={account.profileUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="flex items-center gap-1 text-xs text-primary hover:underline"
        >
          <ExternalLink className="h-3 w-3" />
          {truncate(account.profileUrl, 36)}
        </a>
      ),
    },
    {
      key: "status",
      label: "Status",
      render: (account) => (
        <div className="flex items-center gap-1.5">
          <Badge variant={account.isActive ? "success" : "muted"} className="text-xs">
            {account.isActive ? "Active" : "Inactive"}
          </Badge>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-6 px-2 text-xs"
            onClick={() => openEdit(account)}
          >
            Edit
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-6 px-2 text-xs"
            onClick={() => void handleToggleActive(account.id, account.isActive)}
          >
            {account.isActive ? "Archive" : "Restore"}
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm font-semibold text-foreground">Competitor Accounts</p>
        <div className="flex items-center gap-3">
          <label className="flex items-center gap-2 text-xs text-muted-foreground">
            <Checkbox
              checked={showArchived}
              onChange={(e) => setShowArchived(e.target.checked)}
              className="h-3.5 w-3.5"
            />
            Show archived
          </label>
          <Button type="button" variant="outline" size="sm" onClick={openCreate}>
            <Plus className="h-3.5 w-3.5" /> Add Account
          </Button>
        </div>
      </div>

      <SortableChildTable<ClientCompetitorAccount>
        items={visibleAccounts}
        getId={(item) => item.id}
        columns={columns}
        onReorder={(orderedIds) => void handleReorder(orderedIds)}
        primaryLabel="Principal"
        isLoading={loading ?? loadingAccounts}
        emptyState="No competitor accounts yet. Add one to start tracking."
      />

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Add Competitor Account</DialogTitle>
            <DialogDescription>
              Track a competitor&apos;s social media profile for this client.
            </DialogDescription>
          </DialogHeader>
          <form onSubmit={(e) => void handleCreate(e)} className="space-y-4" noValidate>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-comp-network">
                Social Network <span className="text-destructive">*</span>
              </label>
              <Select
                id="create-comp-network"
                value={createNetworkId}
                onChange={(e) => {
                  setCreateNetworkId(e.target.value);
                  setCreateNetworkError(null);
                  setCreateProfileUrlError(null);
                }}
                className="w-full"
                aria-invalid={createNetworkError ? "true" : "false"}
              >
                <option value="">Select network</option>
                {networks.map((network) => (
                  <option key={network.id} value={network.id}>
                    {network.displayName}
                  </option>
                ))}
              </Select>
              {createNetworkError ? (
                <p className="text-xs text-destructive">{createNetworkError}</p>
              ) : null}
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-comp-url">
                Profile URL <span className="text-destructive">*</span>
              </label>
              <Input
                id="create-comp-url"
                value={createProfileUrl}
                onChange={(e) => {
                  setCreateProfileUrl(e.target.value);
                  setCreateProfileUrlError(null);
                }}
                placeholder="https://www.instagram.com/competitor"
                aria-invalid={createProfileUrlError ? "true" : "false"}
              />
              {createProfileUrlError ? (
                <p className="text-xs text-destructive">{createProfileUrlError}</p>
              ) : null}
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-comp-handle">
                Handle <span className="text-muted-foreground text-xs">(optional)</span>
              </label>
              <Input
                id="create-comp-handle"
                value={createHandle}
                onChange={(e) => setCreateHandle(e.target.value)}
                placeholder="@competitor"
              />
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-comp-label">
                Label <span className="text-muted-foreground text-xs">(optional, max 80)</span>
              </label>
              <Input
                id="create-comp-label"
                value={createLabel}
                onChange={(e) => setCreateLabel(e.target.value)}
                maxLength={80}
                placeholder="Main competitor"
              />
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-comp-notes">
                Notes <span className="text-muted-foreground text-xs">(optional, max 500)</span>
              </label>
              <textarea
                id="create-comp-notes"
                value={createNotes}
                onChange={(e) => setCreateNotes(e.target.value)}
                maxLength={500}
                rows={3}
                placeholder="Notes about this competitor"
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none"
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button type="button" variant="outline" onClick={() => setCreateOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={submittingCreate}>
                {submittingCreate ? "Adding..." : draftMode ? "Add to Draft" : "Add Account"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>

      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Edit Competitor Account</DialogTitle>
            <DialogDescription>
              Update the network, profile URL, handle, label, or notes.
            </DialogDescription>
          </DialogHeader>
          <form onSubmit={(e) => void handleUpdate(e)} className="space-y-4" noValidate>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-comp-network">
                Social Network <span className="text-destructive">*</span>
              </label>
              <Select
                id="edit-comp-network"
                value={editNetworkId}
                onChange={(e) => {
                  setEditNetworkId(e.target.value);
                  setEditNetworkError(null);
                  setEditProfileUrlError(null);
                }}
                className="w-full"
                aria-invalid={editNetworkError ? "true" : "false"}
              >
                <option value="">Select network</option>
                {networks.map((network) => (
                  <option key={network.id} value={network.id}>
                    {network.displayName}
                  </option>
                ))}
              </Select>
              {editNetworkError ? (
                <p className="text-xs text-destructive">{editNetworkError}</p>
              ) : null}
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-comp-url">
                Profile URL <span className="text-destructive">*</span>
              </label>
              <Input
                id="edit-comp-url"
                value={editProfileUrl}
                onChange={(e) => {
                  setEditProfileUrl(e.target.value);
                  setEditProfileUrlError(null);
                }}
                placeholder="https://www.instagram.com/competitor"
                aria-invalid={editProfileUrlError ? "true" : "false"}
              />
              {editProfileUrlError ? (
                <p className="text-xs text-destructive">{editProfileUrlError}</p>
              ) : null}
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-comp-handle">
                Handle <span className="text-muted-foreground text-xs">(optional)</span>
              </label>
              <Input
                id="edit-comp-handle"
                value={editHandle}
                onChange={(e) => setEditHandle(e.target.value)}
                placeholder="@competitor"
              />
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-comp-label">
                Label <span className="text-muted-foreground text-xs">(optional, max 80)</span>
              </label>
              <Input
                id="edit-comp-label"
                value={editLabel}
                onChange={(e) => setEditLabel(e.target.value)}
                maxLength={80}
                placeholder="Main competitor"
              />
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-comp-notes">
                Notes <span className="text-muted-foreground text-xs">(optional, max 500)</span>
              </label>
              <textarea
                id="edit-comp-notes"
                value={editNotes}
                onChange={(e) => setEditNotes(e.target.value)}
                maxLength={500}
                rows={3}
                placeholder="Notes about this competitor"
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none"
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button type="button" variant="outline" onClick={() => setEditOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={submittingEdit}>
                {submittingEdit ? "Saving..." : draftMode ? "Apply to Draft" : "Save Changes"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
