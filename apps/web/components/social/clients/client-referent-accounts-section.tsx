"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import { ExternalLink, Plus } from "lucide-react";
import {
  activateClientReferentAccount,
  createClientReferentAccount,
  deactivateClientReferentAccount,
  listClientReferentAccounts,
  reorderClientReferentAccounts,
  updateClientReferentAccount,
} from "@/lib/api/client-referent-accounts";
import { listSocialNetworks } from "@/lib/api/social-networks";
import type {
  ClientReferentAccount,
  CreateClientReferentAccountPayload,
  SocialNetwork,
  UpdateClientReferentAccountPayload,
} from "@/lib/api/types";
import { getSocialNetworkIcon } from "@/lib/social-network-icons";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
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

function truncate(str: string, max: number): string {
  return str.length <= max ? str : `${str.slice(0, max)}…`;
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

export function ClientReferentAccountsSection({ clientId }: { clientId: string }) {
  const token = useAccessToken();
  const toast = useToast();

  const [accounts, setAccounts] = useState<ClientReferentAccount[]>([]);
  const [networks, setNetworks] = useState<SocialNetwork[]>([]);
  const [loading, setLoading] = useState(true);
  const [showArchived, setShowArchived] = useState(false);

  const [createOpen, setCreateOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [editing, setEditing] = useState<ClientReferentAccount | null>(null);

  const [createNetworkId, setCreateNetworkId] = useState("");
  const [createProfileUrl, setCreateProfileUrl] = useState("");
  const [createHandle, setCreateHandle] = useState("");
  const [createLabel, setCreateLabel] = useState("");
  const [createNotes, setCreateNotes] = useState("");
  const [createProfileUrlError, setCreateProfileUrlError] = useState<string | null>(null);
  const [submittingCreate, setSubmittingCreate] = useState(false);

  const [editProfileUrl, setEditProfileUrl] = useState("");
  const [editHandle, setEditHandle] = useState("");
  const [editLabel, setEditLabel] = useState("");
  const [editNotes, setEditNotes] = useState("");
  const [editProfileUrlError, setEditProfileUrlError] = useState<string | null>(null);
  const [submittingEdit, setSubmittingEdit] = useState(false);

  const effectiveToken = token ?? undefined;

  const loadAccounts = useCallback(async () => {
    if (!effectiveToken) return;
    setLoading(true);
    try {
      const result = await listClientReferentAccounts(clientId, { includeInactive: showArchived }, effectiveToken);
      const sorted = [...result.items].sort((a, b) => a.sortOrder - b.sortOrder);
      setAccounts(sorted);
    } catch {
      toast.error("Referent accounts", "Failed to load referent accounts.");
    } finally {
      setLoading(false);
    }
  }, [clientId, effectiveToken, showArchived, toast]);

  useEffect(() => {
    void loadAccounts();
  }, [loadAccounts]);

  useEffect(() => {
    if (!effectiveToken) return;
    listSocialNetworks({}, effectiveToken)
      .then((result) => setNetworks(result.items))
      .catch(() => {});
  }, [effectiveToken]);

  function openCreate() {
    setCreateNetworkId(networks[0]?.id ?? "");
    setCreateProfileUrl("");
    setCreateHandle("");
    setCreateLabel("");
    setCreateNotes("");
    setCreateProfileUrlError(null);
    setCreateOpen(true);
  }

  function openEdit(account: ClientReferentAccount) {
    setEditing(account);
    setEditProfileUrl(account.profileUrl);
    setEditHandle(account.handle ?? "");
    setEditLabel(account.label ?? "");
    setEditNotes(account.notes ?? "");
    setEditProfileUrlError(null);
    setEditOpen(true);
  }

  async function handleCreate(e: FormEvent) {
    e.preventDefault();
    const urlError = validateProfileUrl(createProfileUrl);
    if (urlError) {
      setCreateProfileUrlError(urlError);
      return;
    }
    setCreateProfileUrlError(null);
    setSubmittingCreate(true);
    try {
      const payload: CreateClientReferentAccountPayload = {
        socialNetworkId: createNetworkId,
        profileUrl: createProfileUrl.trim(),
        handle: createHandle.trim() || undefined,
        label: createLabel.trim() || undefined,
        notes: createNotes.trim() || undefined,
      };
      await createClientReferentAccount(clientId, payload, effectiveToken);
      toast.success("Referent account created", "The referent account has been added.");
      setCreateOpen(false);
      void loadAccounts();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to create referent account.";
      toast.error("Create failed", message);
    } finally {
      setSubmittingCreate(false);
    }
  }

  async function handleUpdate(e: FormEvent) {
    e.preventDefault();
    if (!editing) return;
    const urlError = validateProfileUrl(editProfileUrl);
    if (urlError) {
      setEditProfileUrlError(urlError);
      return;
    }
    setEditProfileUrlError(null);
    setSubmittingEdit(true);
    try {
      const payload: UpdateClientReferentAccountPayload = {
        profileUrl: editProfileUrl.trim(),
        handle: editHandle.trim() || undefined,
        label: editLabel.trim() || undefined,
        notes: editNotes.trim() || undefined,
      };
      await updateClientReferentAccount(clientId, editing.id, payload, effectiveToken);
      toast.success("Referent account updated", "Changes saved successfully.");
      setEditOpen(false);
      void loadAccounts();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to update referent account.";
      toast.error("Update failed", message);
    } finally {
      setSubmittingEdit(false);
    }
  }

  async function handleReorder(orderedIds: string[]) {
    try {
      await reorderClientReferentAccounts(clientId, orderedIds, effectiveToken);
      void loadAccounts();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to reorder accounts.";
      toast.error("Reorder failed", message);
      void loadAccounts();
    }
  }

  async function handleToggleActive(id: string, currentlyActive: boolean) {
    try {
      if (currentlyActive) {
        await deactivateClientReferentAccount(clientId, id, effectiveToken);
        toast.success("Deactivated", "Referent account archived.");
      } else {
        await activateClientReferentAccount(clientId, id, effectiveToken);
        toast.success("Activated", "Referent account reactivated.");
      }
      void loadAccounts();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to update account status.";
      toast.error("Status update failed", message);
    }
  }

  const columns: Column<ClientReferentAccount>[] = [
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
        <p className="text-sm font-semibold text-foreground">Referent Accounts</p>
        <div className="flex items-center gap-3">
          <label className="flex items-center gap-2 text-xs text-muted-foreground">
            <input
              type="checkbox"
              checked={showArchived}
              onChange={(e) => setShowArchived(e.target.checked)}
              className="h-3.5 w-3.5 rounded border-border"
            />
            Show archived
          </label>
          <Button type="button" variant="outline" size="sm" onClick={openCreate}>
            <Plus className="h-3.5 w-3.5" /> Add Account
          </Button>
        </div>
      </div>

      <SortableChildTable<ClientReferentAccount>
        items={accounts}
        getId={(item) => item.id}
        columns={columns}
        onReorder={(orderedIds) => void handleReorder(orderedIds)}
        primaryLabel="Principal"
        isLoading={loading}
        emptyState="No referent accounts yet. Add one to start tracking."
      />

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Add Referent Account</DialogTitle>
            <DialogDescription>
              Track a social media profile for this client.
            </DialogDescription>
          </DialogHeader>
          <form onSubmit={(e) => void handleCreate(e)} className="space-y-4">
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-network">
                Social Network
              </label>
              <Select
                id="create-network"
                value={createNetworkId}
                onChange={(e) => setCreateNetworkId(e.target.value)}
                className="w-full"
                required
              >
                {networks.map((n) => (
                  <option key={n.id} value={n.id}>
                    {n.displayName}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-url">
                Profile URL <span className="text-destructive">*</span>
              </label>
              <Input
                id="create-url"
                value={createProfileUrl}
                onChange={(e) => {
                  setCreateProfileUrl(e.target.value);
                  setCreateProfileUrlError(null);
                }}
                placeholder="https://www.instagram.com/brand"
                required
              />
              {createProfileUrlError ? (
                <p className="text-xs text-destructive">{createProfileUrlError}</p>
              ) : null}
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-handle">
                Handle <span className="text-muted-foreground text-xs">(optional)</span>
              </label>
              <Input
                id="create-handle"
                value={createHandle}
                onChange={(e) => setCreateHandle(e.target.value)}
                placeholder="@brand"
              />
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-label">
                Label <span className="text-muted-foreground text-xs">(optional, max 80)</span>
              </label>
              <Input
                id="create-label"
                value={createLabel}
                onChange={(e) => setCreateLabel(e.target.value)}
                maxLength={80}
                placeholder="Main account"
              />
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="create-notes">
                Notes <span className="text-muted-foreground text-xs">(optional, max 500)</span>
              </label>
              <textarea
                id="create-notes"
                value={createNotes}
                onChange={(e) => setCreateNotes(e.target.value)}
                maxLength={500}
                rows={3}
                placeholder="Competitor account, track weekly"
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none"
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button type="button" variant="outline" onClick={() => setCreateOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={submittingCreate}>
                {submittingCreate ? "Adding…" : "Add Account"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>

      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Edit Referent Account</DialogTitle>
            <DialogDescription>
              Update profile URL, handle, label, or notes. The social network cannot be changed.
            </DialogDescription>
          </DialogHeader>
          <form onSubmit={(e) => void handleUpdate(e)} className="space-y-4">
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-url">
                Profile URL <span className="text-destructive">*</span>
              </label>
              <Input
                id="edit-url"
                value={editProfileUrl}
                onChange={(e) => {
                  setEditProfileUrl(e.target.value);
                  setEditProfileUrlError(null);
                }}
                placeholder="https://www.instagram.com/brand"
                required
              />
              {editProfileUrlError ? (
                <p className="text-xs text-destructive">{editProfileUrlError}</p>
              ) : null}
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-handle">
                Handle <span className="text-muted-foreground text-xs">(optional)</span>
              </label>
              <Input
                id="edit-handle"
                value={editHandle}
                onChange={(e) => setEditHandle(e.target.value)}
                placeholder="@brand"
              />
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-label">
                Label <span className="text-muted-foreground text-xs">(optional, max 80)</span>
              </label>
              <Input
                id="edit-label"
                value={editLabel}
                onChange={(e) => setEditLabel(e.target.value)}
                maxLength={80}
                placeholder="Main account"
              />
            </div>
            <div className="space-y-1">
              <label className="text-sm font-medium" htmlFor="edit-notes">
                Notes <span className="text-muted-foreground text-xs">(optional, max 500)</span>
              </label>
              <textarea
                id="edit-notes"
                value={editNotes}
                onChange={(e) => setEditNotes(e.target.value)}
                maxLength={500}
                rows={3}
                placeholder="Notes about this account"
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none"
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button type="button" variant="outline" onClick={() => setEditOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={submittingEdit}>
                {submittingEdit ? "Saving…" : "Save Changes"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
