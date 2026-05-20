"use client";

import { FormEvent, useState } from "react";
import { createSocialClient, updateSocialClient } from "@/lib/api/social-clients";
import { ApiError, SocialClient } from "@/lib/api/types";

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
  submitting: boolean;
  setName: (value: string) => void;
  setDescription: (value: string) => void;
  setSectorOrNiche: (value: string | null) => void;
  openCreate: () => void;
  openEdit: (item: SocialClient) => void;
  closeForm: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void>;
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
  const [submitting, setSubmitting] = useState(false);

  function openCreate() {
    setMode("create");
    setSelected(null);
    setName("");
    setDescription("");
    setSectorOrNiche(null);
    setFormOpen(true);
  }

  function openEdit(item: SocialClient) {
    setMode("edit");
    setSelected(item);
    setName(item.name);
    setDescription(item.description ?? "");
    setSectorOrNiche(null);
    setFormOpen(true);
  }

  function closeForm() {
    setFormOpen(false);
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);

    try {
      const payload = {
        name,
        description: description.trim() || null,
        sectorOrNiche: sectorOrNiche ?? null,
      };

      const saved =
        mode === "create"
          ? await createSocialClient(payload, effectiveToken || undefined)
          : selected
            ? await updateSocialClient(selected.id, payload, effectiveToken || undefined)
            : null;

      setFormOpen(false);
      setName("");
      setDescription("");
      setSectorOrNiche(null);

      if (saved) {
        onSuccess(saved, mode);
      }
    } catch (err) {
      onError((err as ApiError).message);
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
    submitting,
    setName,
    setDescription,
    setSectorOrNiche,
    openCreate,
    openEdit,
    closeForm,
    onSubmit,
  };
}
