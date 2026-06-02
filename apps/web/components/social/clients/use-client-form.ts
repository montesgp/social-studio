"use client";

import { FormEvent, useState } from "react";
import {
  listClientReferentAccounts,
  syncClientReferentAccounts,
} from "@/lib/api/client-referent-accounts";
import {
  listClientCompetitorAccounts,
  syncClientCompetitorAccounts,
} from "@/lib/api/client-competitor-accounts";
import {
  listClientOwnAccounts,
  syncClientOwnAccounts,
} from "@/lib/api/client-own-accounts";
import { createSocialClient, updateSocialClient } from "@/lib/api/social-clients";
import {
  ApiError,
  ClientCompetitorAccount,
  ClientOwnAccount,
  ClientReferentAccount,
  SocialClient,
  SyncClientCompetitorAccountPayload,
  SyncClientOwnAccountPayload,
  SyncClientReferentAccountPayload,
} from "@/lib/api/types";

type Mode = "create" | "edit";

interface UseClientFormOptions {
  effectiveToken: string;
  onSuccess: (saved: SocialClient, mode: Mode) => void;
  onError: (message: string) => void;
}

interface UseClientFormReturn {
  formOpen: boolean;
  setFormOpen: (open: boolean) => void;
  mode: Mode;
  selected: SocialClient | null;
  name: string;
  description: string;
  sectorOrNiche: string | null;
  fieldErrors: ClientFormFieldErrors;
  referentAccounts: ClientReferentAccount[];
  referentAccountsLoading: boolean;
  competitorAccounts: ClientCompetitorAccount[];
  competitorAccountsLoading: boolean;
  ownAccounts: ClientOwnAccount[];
  ownAccountsLoading: boolean;
  submitting: boolean;
  setName: (value: string) => void;
  setDescription: (value: string) => void;
  setSectorOrNiche: (value: string | null) => void;
  setReferentAccounts: (value: ClientReferentAccount[]) => void;
  setCompetitorAccounts: (value: ClientCompetitorAccount[]) => void;
  setOwnAccounts: (value: ClientOwnAccount[]) => void;
  openCreate: () => void;
  openEdit: (item: SocialClient) => void;
  closeForm: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void>;
}

export interface ClientFormFieldErrors {
  name?: string;
  description?: string;
  sectorOrNiche?: string;
  referentAccounts?: string;
}

const CLIENT_NAME_MAX_LENGTH = 200;
const CLIENT_DESCRIPTION_MAX_LENGTH = 1000;
const CLIENT_SECTOR_OR_NICHE_MAX_LENGTH = 300;

function isDraftId(id: string) {
  return id.startsWith("draft-");
}

function toReferentAccountPayload(account: ClientReferentAccount): SyncClientReferentAccountPayload {
  return {
    id: isDraftId(account.id) ? undefined : account.id,
    socialNetworkId: account.socialNetworkId,
    profileUrl: account.profileUrl.trim(),
    handle: account.handle?.trim() || undefined,
    label: account.label?.trim() || undefined,
    notes: account.notes?.trim() || undefined,
    isActive: account.isActive,
  };
}

function toCompetitorAccountPayload(account: ClientCompetitorAccount): SyncClientCompetitorAccountPayload {
  return {
    id: isDraftId(account.id) ? undefined : account.id,
    socialNetworkId: account.socialNetworkId,
    profileUrl: account.profileUrl.trim(),
    handle: account.handle?.trim() || undefined,
    label: account.label?.trim() || undefined,
    notes: account.notes?.trim() || undefined,
    isActive: account.isActive,
  };
}

function toOwnAccountPayload(account: ClientOwnAccount): SyncClientOwnAccountPayload {
  return {
    id: isDraftId(account.id) ? undefined : account.id,
    socialNetworkId: account.socialNetworkId,
    profileUrl: account.profileUrl.trim(),
    handle: account.handle?.trim() || undefined,
    label: account.label?.trim() || undefined,
    notes: account.notes?.trim() || undefined,
    isActive: account.isActive,
  };
}

export function useClientForm({
  effectiveToken,
  onSuccess,
  onError,
}: UseClientFormOptions): UseClientFormReturn {
  const [formOpen, setFormOpen] = useState(false);
  const [mode, setMode] = useState<Mode>("create");
  const [selected, setSelected] = useState<SocialClient | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [sectorOrNiche, setSectorOrNiche] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<ClientFormFieldErrors>({});
  const [referentAccounts, setReferentAccountsState] = useState<ClientReferentAccount[]>([]);
  const [referentAccountsLoading, setReferentAccountsLoading] = useState(false);
  const [competitorAccounts, setCompetitorAccountsState] = useState<ClientCompetitorAccount[]>([]);
  const [competitorAccountsLoading, setCompetitorAccountsLoading] = useState(false);
  const [ownAccounts, setOwnAccountsState] = useState<ClientOwnAccount[]>([]);
  const [ownAccountsLoading, setOwnAccountsLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  function clearFieldError(field: keyof ClientFormFieldErrors) {
    setFieldErrors((current) => {
      if (!current[field]) return current;
      const next = { ...current };
      delete next[field];
      return next;
    });
  }

  function updateName(value: string) {
    setName(value);
    clearFieldError("name");
  }

  function updateDescription(value: string) {
    setDescription(value);
    clearFieldError("description");
  }

  function updateSectorOrNiche(value: string | null) {
    setSectorOrNiche(value);
    clearFieldError("sectorOrNiche");
  }

  function setReferentAccounts(value: ClientReferentAccount[]) {
    setReferentAccountsState(value);
    clearFieldError("referentAccounts");
  }

  function setCompetitorAccounts(value: ClientCompetitorAccount[]) {
    setCompetitorAccountsState(value);
  }

  function setOwnAccounts(value: ClientOwnAccount[]) {
    setOwnAccountsState(value);
  }

  function openCreate() {
    setMode("create");
    setSelected(null);
    setName("");
    setDescription("");
    setSectorOrNiche(null);
    setFieldErrors({});
    setReferentAccountsState([]);
    setCompetitorAccountsState([]);
    setOwnAccountsState([]);
    setFormOpen(true);
  }

  function openEdit(item: SocialClient) {
    setMode("edit");
    setSelected(item);
    setName(item.name);
    setDescription(item.description ?? "");
    setSectorOrNiche(item.sectorOrNiche ?? null);
    setFieldErrors({});
    setReferentAccountsState([]);
    setCompetitorAccountsState([]);
    setOwnAccountsState([]);
    setFormOpen(true);
    void loadReferentAccounts(item.id);
    void loadCompetitorAccounts(item.id);
    void loadOwnAccounts(item.id);
  }

  function closeForm() {
    setFormOpen(false);
  }

  async function loadReferentAccounts(clientId: string) {
    setReferentAccountsLoading(true);
    try {
      const result = await listClientReferentAccounts(
        clientId,
        { includeInactive: true, page: 1, pageSize: 200 },
        effectiveToken || undefined,
      );
      setReferentAccountsState([...result.items].sort((a, b) => a.sortOrder - b.sortOrder));
    } catch (err) {
      const message = (err as ApiError).message ?? "Failed to load referent accounts.";
      setFieldErrors((current) => ({ ...current, referentAccounts: message }));
      onError(message);
    } finally {
      setReferentAccountsLoading(false);
    }
  }

  async function loadCompetitorAccounts(clientId: string) {
    setCompetitorAccountsLoading(true);
    try {
      const result = await listClientCompetitorAccounts(
        clientId,
        { includeInactive: true, page: 1, pageSize: 200 },
        effectiveToken || undefined,
      );
      setCompetitorAccountsState([...result.items].sort((a, b) => a.sortOrder - b.sortOrder));
    } catch (err) {
      const message = (err as ApiError).message ?? "Failed to load competitor accounts.";
      onError(message);
    } finally {
      setCompetitorAccountsLoading(false);
    }
  }

  async function loadOwnAccounts(clientId: string) {
    setOwnAccountsLoading(true);
    try {
      const result = await listClientOwnAccounts(
        clientId,
        { includeInactive: true, page: 1, pageSize: 200 },
        effectiveToken || undefined,
      );
      setOwnAccountsState([...result.items].sort((a, b) => a.sortOrder - b.sortOrder));
    } catch (err) {
      const message = (err as ApiError).message ?? "Failed to load own accounts.";
      onError(message);
    } finally {
      setOwnAccountsLoading(false);
    }
  }

  function validate(): ClientFormFieldErrors {
    const errors: ClientFormFieldErrors = {};
    if (!name.trim()) {
      errors.name = "Name is required.";
    } else if (name.trim().length > CLIENT_NAME_MAX_LENGTH) {
      errors.name = `Name must not exceed ${CLIENT_NAME_MAX_LENGTH} characters.`;
    }

    if (description.length > CLIENT_DESCRIPTION_MAX_LENGTH) {
      errors.description = `Description must not exceed ${CLIENT_DESCRIPTION_MAX_LENGTH} characters.`;
    }

    if ((sectorOrNiche ?? "").length > CLIENT_SECTOR_OR_NICHE_MAX_LENGTH) {
      errors.sectorOrNiche = `Sector / Niche must not exceed ${CLIENT_SECTOR_OR_NICHE_MAX_LENGTH} characters.`;
    }

    if (mode === "edit" && fieldErrors.referentAccounts) {
      errors.referentAccounts = fieldErrors.referentAccounts;
    }

    return errors;
  }

  function applyApiFieldErrors(error: ApiError) {
    const errors = error.problem?.errors;
    if (!errors || typeof errors !== "object") return;

    const next: ClientFormFieldErrors = {};
    const entries = Object.entries(errors as Record<string, unknown>);
    for (const [field, value] of entries) {
      const message = Array.isArray(value) ? value.filter((x): x is string => typeof x === "string").join(" ") : String(value);
      const normalized = field.toLowerCase();
      if (normalized.includes("name")) next.name = message;
      if (normalized.includes("description")) next.description = message;
      if (normalized.includes("sectororniche")) next.sectorOrNiche = message;
    }

    if (Object.keys(next).length > 0) {
      setFieldErrors((current) => ({ ...current, ...next }));
    }
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const validationErrors = validate();
    if (Object.keys(validationErrors).length > 0) {
      setFieldErrors(validationErrors);
      return;
    }

    setSubmitting(true);

    try {
      const payload = {
        name: name.trim(),
        description: description.trim() || null,
        sectorOrNiche: sectorOrNiche?.trim() || null,
      };

      const saved =
        mode === "create"
          ? await createSocialClient(payload, effectiveToken || undefined)
          : selected
            ? await updateSocialClient(selected.id, payload, effectiveToken || undefined)
            : null;

      if (saved && mode === "edit") {
        const [syncedReferent, syncedCompetitor, syncedOwn] = await Promise.all([
          syncClientReferentAccounts(
            saved.id,
            referentAccounts.map(toReferentAccountPayload),
            effectiveToken || undefined,
          ),
          syncClientCompetitorAccounts(
            saved.id,
            competitorAccounts.map(toCompetitorAccountPayload),
            effectiveToken || undefined,
          ),
          syncClientOwnAccounts(
            saved.id,
            ownAccounts.map(toOwnAccountPayload),
            effectiveToken || undefined,
          ),
        ]);
        setReferentAccountsState([...syncedReferent].sort((a, b) => a.sortOrder - b.sortOrder));
        setCompetitorAccountsState([...syncedCompetitor].sort((a, b) => a.sortOrder - b.sortOrder));
        setOwnAccountsState([...syncedOwn].sort((a, b) => a.sortOrder - b.sortOrder));
      }

      setFormOpen(false);
      setName("");
      setDescription("");
      setSectorOrNiche(null);
      setFieldErrors({});
      setReferentAccountsState([]);
      setCompetitorAccountsState([]);
      setOwnAccountsState([]);

      if (saved) {
        onSuccess(saved, mode);
      }
    } catch (err) {
      const apiError = err as ApiError;
      applyApiFieldErrors(apiError);
      onError(apiError.message);
    } finally {
      setSubmitting(false);
    }
  }

  return {
    formOpen,
    setFormOpen,
    mode,
    selected,
    name,
    description,
    sectorOrNiche,
    fieldErrors,
    referentAccounts,
    referentAccountsLoading,
    competitorAccounts,
    competitorAccountsLoading,
    ownAccounts,
    ownAccountsLoading,
    submitting,
    setName: updateName,
    setDescription: updateDescription,
    setSectorOrNiche: updateSectorOrNiche,
    setReferentAccounts,
    setCompetitorAccounts,
    setOwnAccounts,
    openCreate,
    openEdit,
    closeForm,
    onSubmit,
  };
}
