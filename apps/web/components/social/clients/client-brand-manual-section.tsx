"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import { useDateFormatter } from "@/lib/format/use-date-formatter";
import { HexColorPicker } from "react-colorful";
import {
  Plus,
  History,
  Palette,
  Image as ImageIcon,
  Type,
  FileText,
  Trash2,
  X,
} from "lucide-react";
import {
  activateClientBrandManual,
  createClientBrandManual,
  deactivateClientBrandManual,
  listClientBrandManuals,
  updateClientBrandManual,
} from "@/lib/api/client-brand-manuals";
import { uploadClientImageAsset } from "@/lib/api/client-assets";
import type {
  BackgroundEntry,
  ClientBrandManual,
  ColorEntry,
  CreateClientBrandManualPayload,
  LogoEntry,
  TypographyEntry,
  UpdateClientBrandManualPayload,
} from "@/lib/api/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ImageUploadField } from "@/components/ui/image-upload-field";
import { Input } from "@/components/ui/input";
import { useToast } from "@/components/ui/toaster";
import { useAccessToken } from "@/components/auth/use-access-token";
import { cn } from "@/lib/utils";

const HEX_REGEX = /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/;
const COLOR_SWATCHES = [
  "#111827",
  "#ffffff",
  "#ef4444",
  "#f97316",
  "#f59e0b",
  "#84cc16",
  "#22c55e",
  "#14b8a6",
  "#06b6d4",
  "#3b82f6",
  "#6366f1",
  "#8b5cf6",
  "#a855f7",
  "#ec4899",
  "#f43f5e",
  "#78716c",
];
const COMMON_FONT_FAMILIES = [
  "Inter",
  "Roboto",
  "Open Sans",
  "Montserrat",
  "Poppins",
  "Lato",
  "Raleway",
  "Nunito",
  "Source Sans 3",
  "Merriweather",
  "Playfair Display",
  "Oswald",
  "Bebas Neue",
  "Arial",
  "Helvetica",
  "Georgia",
  "Times New Roman",
  "Verdana",
];

const SYSTEM_FONT_FAMILIES = new Set([
  "Arial",
  "Helvetica",
  "Georgia",
  "Times New Roman",
  "Verdana",
]);
const FONT_PREVIEW_STYLESHEET_ID = "client-brand-manual-font-preview";
const FONT_PREVIEW_STYLESHEET_URL = `https://fonts.googleapis.com/css2?${COMMON_FONT_FAMILIES
  .filter((fontFamily) => !SYSTEM_FONT_FAMILIES.has(fontFamily))
  .map((fontFamily) => `family=${fontFamily.replace(/\s+/g, "+")}:wght@400`)
  .join("&")}&display=swap`;

const FONT_PREVIEW_STACKS: Record<string, string> = {
  Inter: "Inter, ui-sans-serif, system-ui, sans-serif",
  Roboto: "Roboto, Arial, sans-serif",
  "Open Sans": "'Open Sans', Arial, sans-serif",
  Montserrat: "Montserrat, Arial, sans-serif",
  Poppins: "Poppins, Arial, sans-serif",
  Lato: "Lato, Arial, sans-serif",
  Raleway: "Raleway, Arial, sans-serif",
  Nunito: "Nunito, Arial, sans-serif",
  "Source Sans 3": "'Source Sans 3', Arial, sans-serif",
  Merriweather: "Merriweather, Georgia, serif",
  "Playfair Display": "'Playfair Display', Georgia, serif",
  Oswald: "Oswald, Arial, sans-serif",
  "Bebas Neue": "'Bebas Neue', Impact, sans-serif",
  Arial: "Arial, sans-serif",
  Helvetica: "Helvetica, Arial, sans-serif",
  Georgia: "Georgia, serif",
  "Times New Roman": "'Times New Roman', Times, serif",
  Verdana: "Verdana, Geneva, sans-serif",
};

function validateHex(hex: string): string | null {
  if (!hex.trim()) return "Hex color is required.";
  if (!HEX_REGEX.test(hex.trim())) return "Invalid hex (#RGB, #RRGGBB, or #RRGGBBAA).";
  return null;
}

function validateHttpUrl(url: string): string | null {
  const trimmed = url.trim();
  if (!trimmed) return "URL is required.";
  if (trimmed.length < 10) return "URL must be at least 10 characters.";
  if (trimmed.length > 2048) return "URL must not exceed 2048 characters.";
  if (!trimmed.startsWith("http://") && !trimmed.startsWith("https://"))
    return "URL must start with http:// or https://";
  return null;
}

function truncate(str: string, max: number): string {
  return str.length <= max ? str : `${str.slice(0, max)}…`;
}

function hasSignal(
  palette: ColorEntry[],
  logos: LogoEntry[],
  bg: BackgroundEntry[],
  typo: TypographyEntry[],
  voice: string,
  notes: string,
): boolean {
  return (
    palette.length > 0 ||
    logos.length > 0 ||
    bg.length > 0 ||
    typo.length > 0 ||
    voice.trim().length > 0 ||
    notes.trim().length > 0
  );
}

function hasInvalidStructuredEntries(
  palette: ColorEntry[],
  logos: LogoEntry[],
  bg: BackgroundEntry[],
  typo: TypographyEntry[],
): boolean {
  return (
    palette.some((entry) => !entry.name.trim() || Boolean(validateHex(entry.hex))) ||
    logos.some((entry) => !entry.name.trim() || Boolean(validateHttpUrl(entry.url))) ||
    bg.some((entry) => !entry.name.trim() || Boolean(validateHttpUrl(entry.url))) ||
    typo.some((entry) => !entry.fontFamily.trim())
  );
}

function getFontPreviewStack(fontFamily: string): string {
  const trimmed = fontFamily.trim();
  return FONT_PREVIEW_STACKS[trimmed] ?? `${trimmed}, ui-sans-serif, system-ui, sans-serif`;
}

function createTypographyEntry(fontFamily: string): TypographyEntry {
  const family = fontFamily.trim();
  return {
    name: family,
    fontFamily: family,
    role: null,
    fontUrl: null,
    weight: null,
  };
}

function normalizeTypographyEntries(entries: TypographyEntry[]): TypographyEntry[] {
  return entries
    .map((entry) => {
      const fontFamily = entry.fontFamily.trim();
      return {
        ...entry,
        name: entry.name.trim() || fontFamily,
        fontFamily,
      };
    })
    .filter((entry) => entry.fontFamily.length > 0);
}

// --- Types and helpers ---

type BrandManualDraft = {
  title: string;
  palette: ColorEntry[];
  logos: LogoEntry[];
  bg: BackgroundEntry[];
  typo: TypographyEntry[];
  voice: string;
  notes: string;
};

function createEmptyDraft(): BrandManualDraft {
  return { title: "", palette: [], logos: [], bg: [], typo: [], voice: "", notes: "" };
}

function updateAt<T>(list: T[], i: number, item: T): T[] {
  return list.map((x, idx) => (idx === i ? item : x));
}

function removeAt<T>(list: T[], i: number): T[] {
  return list.filter((_, idx) => idx !== i);
}

// --- Row editors ---

function toSafeHex(hex: string): string {
  const h = hex.trim();
  if (/^#[0-9a-fA-F]{3}$/.test(h)) return `#${h[1]}${h[1]}${h[2]}${h[2]}${h[3]}${h[3]}`;
  if (/^#[0-9a-fA-F]{6}$/.test(h)) return h;
  if (/^#[0-9a-fA-F]{8}$/.test(h)) return h.slice(0, 7);
  return "#000000";
}

const COLOR_ROLE_OPTIONS = [
  { value: "", label: "Unclassified" },
  { value: "Primary", label: "Primary" },
  { value: "Secondary", label: "Secondary" },
  { value: "Accent", label: "Accent" },
  { value: "Neutral", label: "Neutral" },
];

function ColorRow({
  entry,
  showValidationErrors,
  onChange,
  onRemove,
}: {
  entry: ColorEntry;
  showValidationErrors: boolean;
  onChange: (e: ColorEntry) => void;
  onRemove: () => void;
}) {
  const [pickerOpen, setPickerOpen] = useState(false);
  const nameError = showValidationErrors && !entry.name.trim() ? "Name is required." : null;
  const hexError = showValidationErrors || entry.hex ? validateHex(entry.hex) : null;
  return (
    <div className="space-y-2 rounded-md border border-border/60 bg-muted/10 p-3">
      <div className="flex flex-wrap items-start gap-2">
        <div className="min-w-40 flex-1 space-y-1">
          <Input
            placeholder="Name"
            value={entry.name}
            onChange={(e) => onChange({ ...entry, name: e.target.value })}
            aria-invalid={nameError ? "true" : "false"}
          />
          {nameError ? <p className="text-xs text-destructive">{nameError}</p> : null}
        </div>
        <div className="flex flex-col gap-1">
          <div className="flex items-center gap-1">
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-9 w-9 shrink-0 p-0"
              style={{ backgroundColor: toSafeHex(entry.hex) }}
              onClick={() => setPickerOpen((open) => !open)}
              aria-label="Open color picker"
            />
            <Input
              placeholder="#FF0000"
              value={entry.hex}
              onChange={(e) => onChange({ ...entry, hex: e.target.value })}
              className="w-28"
              aria-invalid={hexError ? "true" : "false"}
            />
          </div>
          {hexError && <p className="text-xs text-destructive">{hexError}</p>}
        </div>
        <div className="flex flex-col gap-1">
          <select
            value={entry.role ?? ""}
            onChange={(e) => onChange({ ...entry, role: e.target.value || null })}
            className="h-9 rounded-md border border-input bg-background px-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring w-32"
            aria-label="Color role"
          >
            {COLOR_ROLE_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <Input
            type="number"
            min={0}
            max={99}
            value={entry.rank ?? 0}
            onChange={(e) => onChange({ ...entry, rank: Math.max(0, parseInt(e.target.value, 10) || 0) })}
            className="w-16"
            aria-label="Rank within role (lower = higher priority)"
            title="Rank (0 = highest priority within role)"
          />
        </div>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-9 w-9 p-0"
          onClick={onRemove}
          aria-label="Remove color"
        >
          <Trash2 className="h-4 w-4" />
        </Button>
      </div>

      {pickerOpen ? (
        <div className="rounded-md border border-border bg-background p-3 shadow-sm space-y-3">
          <div className="flex items-center justify-between gap-2">
            <span className="text-xs font-medium text-muted-foreground">Color picker</span>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-8 w-8 p-0"
              onClick={() => setPickerOpen(false)}
              aria-label="Close color picker"
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
          <HexColorPicker
            color={toSafeHex(entry.hex)}
            onChange={(hex) => onChange({ ...entry, hex })}
            style={{ width: "100%" }}
          />
          <div className="grid grid-cols-8 gap-1.5">
            {COLOR_SWATCHES.map((swatch) => (
              <Button
                key={swatch}
                type="button"
                variant="outline"
                size="sm"
                className="h-8 w-8 p-0"
                style={{ backgroundColor: swatch }}
                onClick={() => onChange({ ...entry, hex: swatch })}
                aria-label={`Use ${swatch}`}
              />
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

function ImageAssetRow({
  name,
  url,
  extra,
  extraPlaceholder,
  assetLabel,
  showValidationErrors,
  onChangeName,
  onChangeUrl,
  onChangeExtra,
  onUpload,
  onRemove,
}: {
  name: string;
  url: string;
  extra: string | null;
  extraPlaceholder: string;
  assetLabel: string;
  showValidationErrors: boolean;
  onChangeName: (v: string) => void;
  onChangeUrl: (v: string) => void;
  onChangeExtra: (v: string | null) => void;
  onUpload: (file: File) => Promise<string>;
  onRemove: () => void;
}) {
  const nameError = showValidationErrors && !name.trim() ? "Name is required." : null;
  const urlError = showValidationErrors || url ? validateHttpUrl(url) : null;

  return (
    <div className="grid gap-2 rounded-md border border-border/60 bg-muted/20 p-3">
      <div className="flex gap-2 items-start flex-wrap">
        <div className="flex-1 min-w-24 space-y-1">
          <Input
            placeholder="Name"
            value={name}
            onChange={(e) => onChangeName(e.target.value)}
            aria-invalid={nameError ? "true" : "false"}
          />
          {nameError ? <p className="text-xs text-destructive">{nameError}</p> : null}
        </div>
        <Input
          placeholder={extraPlaceholder}
          value={extra ?? ""}
          onChange={(e) => onChangeExtra(e.target.value || null)}
          className="w-32"
        />
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-9 w-9 p-0"
          onClick={onRemove}
          aria-label="Remove image asset"
        >
          <Trash2 className="h-4 w-4" />
        </Button>
      </div>
      <ImageUploadField
        value={url}
        onChange={onChangeUrl}
        onUpload={onUpload}
        previewAlt={assetLabel}
      />
      {urlError ? <p className="text-xs text-destructive">{urlError}</p> : null}
    </div>
  );
}

function SelectedFontCard({
  entry,
  onRemove,
}: {
  entry: TypographyEntry;
  onRemove: () => void;
}) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-md border border-border bg-card/70 p-3">
      <div className="min-w-0">
        <p
          className="truncate text-lg leading-tight text-foreground"
          style={{ fontFamily: getFontPreviewStack(entry.fontFamily) }}
        >
          Aa Brand voice
        </p>
        <p className="mt-1 truncate text-xs text-muted-foreground">{entry.fontFamily}</p>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="h-9 w-9 p-0"
        onClick={onRemove}
        aria-label="Remove font"
      >
        <Trash2 className="h-4 w-4" />
      </Button>
    </div>
  );
}

// --- Main section ---

export function ClientBrandManualSection({ clientId }: { clientId: string }) {
  const { formatDate } = useDateFormatter();
  const token = useAccessToken();
  const toast = useToast();
  const effectiveToken = token ?? undefined;

  const [active, setActive] = useState<ClientBrandManual | null>(null);
  const [history, setHistory] = useState<ClientBrandManual[]>([]);
  const [loading, setLoading] = useState(true);
  const [showHistory, setShowHistory] = useState(false);
  const [acting, setActing] = useState<string | null>(null);

  const [dialogOpen, setDialogOpen] = useState(false);
  const [dialogMode, setDialogMode] = useState<"create" | "edit">("create");
  const [editing, setEditing] = useState<ClientBrandManual | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [showValidationErrors, setShowValidationErrors] = useState(false);
  const [titleError, setTitleError] = useState<string | null>(null);
  const [signalError, setSignalError] = useState<string | null>(null);
  const [fontPickerOpen, setFontPickerOpen] = useState(false);
  const [customFontFamily, setCustomFontFamily] = useState("");

  const [form, setForm] = useState<BrandManualDraft>(createEmptyDraft);

  useEffect(() => {
    if (typeof document === "undefined") return;
    if (document.getElementById(FONT_PREVIEW_STYLESHEET_ID)) return;

    const link = document.createElement("link");
    link.id = FONT_PREVIEW_STYLESHEET_ID;
    link.rel = "stylesheet";
    link.href = FONT_PREVIEW_STYLESHEET_URL;
    document.head.appendChild(link);
  }, []);

  const handleImageUpload = useCallback(
    async (file: File) => {
      if (!effectiveToken) {
        throw new Error("Tu sesión expiró. Vuelve a iniciar sesión.");
      }

      const uploaded = await uploadClientImageAsset(clientId, file, effectiveToken);
      return uploaded.url;
    },
    [clientId, effectiveToken],
  );

  const reload = useCallback(async () => {
    if (!effectiveToken) return;
    setLoading(true);
    try {
      const activePage = await listClientBrandManuals(
        clientId,
        { includeInactive: false, page: 1, pageSize: 1 },
        effectiveToken,
      );
      setActive(activePage.items[0] ?? null);
    } catch {
      setActive(null);
    } finally {
      setLoading(false);
    }
  }, [clientId, effectiveToken]);

  const loadHistory = useCallback(async () => {
    if (!effectiveToken) return;
    try {
      const result = await listClientBrandManuals(
        clientId,
        { includeInactive: true, page: 1, pageSize: 50 },
        effectiveToken,
      );
      setHistory(result.items.filter((m) => !m.isActive));
    } catch {
      toast.error("Brand Manual", "Failed to load history.");
    }
  }, [clientId, effectiveToken, toast]);

  useEffect(() => {
    void reload();
  }, [reload]);

  useEffect(() => {
    if (showHistory) void loadHistory();
  }, [showHistory, loadHistory]);

  function openCreate() {
    setDialogMode("create");
    setEditing(null);
    setForm(createEmptyDraft());
    setShowValidationErrors(false);
    setTitleError(null);
    setSignalError(null);
    setFontPickerOpen(false);
    setCustomFontFamily("");
    setDialogOpen(true);
  }

  function openEdit(manual: ClientBrandManual) {
    setDialogMode("edit");
    setEditing(manual);
    setForm({
      title: manual.title,
      palette: [...manual.colorPalette],
      logos: [...manual.logos],
      bg: [...manual.backgroundTemplates],
      typo: [...manual.typography],
      voice: manual.voiceAndTone ?? "",
      notes: manual.notes ?? "",
    });
    setShowValidationErrors(false);
    setTitleError(null);
    setSignalError(null);
    setFontPickerOpen(false);
    setCustomFontFamily("");
    setDialogOpen(true);
  }

  function addFont(fontFamily: string) {
    const trimmed = fontFamily.trim();
    if (!trimmed) return;
    if (form.typo.some((e) => e.fontFamily.trim().toLowerCase() === trimmed.toLowerCase())) return;
    setForm((prev) => ({ ...prev, typo: [...prev.typo, createTypographyEntry(trimmed)] }));
    setCustomFontFamily("");
    setSignalError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    e.stopPropagation();
    setShowValidationErrors(true);

    const nextTitleError = form.title.trim() ? null : "Title is required.";
    setTitleError(nextTitleError);

    if (!hasSignal(form.palette, form.logos, form.bg, form.typo, form.voice, form.notes)) {
      setSignalError(
        "At least one field is required (palette, logos, backgrounds, typography, voice, or notes).",
      );
      return;
    }

    if (hasInvalidStructuredEntries(form.palette, form.logos, form.bg, form.typo)) {
      setSignalError("Complete the required fields before saving the brand manual.");
      return;
    }

    if (nextTitleError) {
      setSignalError(null);
      return;
    }

    setSignalError(null);
    setSubmitting(true);
    try {
      const payload: CreateClientBrandManualPayload | UpdateClientBrandManualPayload = {
        title: form.title.trim(),
        colorPalette: form.palette,
        logos: form.logos,
        backgroundTemplates: form.bg,
        typography: normalizeTypographyEntries(form.typo),
        voiceAndTone: form.voice.trim() || undefined,
        notes: form.notes.trim() || undefined,
      };
      if (dialogMode === "create") {
        await createClientBrandManual(clientId, payload as CreateClientBrandManualPayload, effectiveToken);
        toast.success("Brand Manual", "Brand manual created.");
      } else if (editing) {
        await updateClientBrandManual(clientId, editing.id, payload as UpdateClientBrandManualPayload, effectiveToken);
        toast.success("Brand Manual", "Brand manual updated.");
      }
      setDialogOpen(false);
      void reload();
      if (showHistory) void loadHistory();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to save brand manual.";
      setSignalError(message);
      toast.error("Save failed", message);
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDeactivate(manual: ClientBrandManual) {
    if (!effectiveToken) return;
    setActing(manual.id);
    try {
      await deactivateClientBrandManual(clientId, manual.id, effectiveToken);
      toast.success("Brand Manual", "Manual deactivated.");
      void reload();
      if (showHistory) void loadHistory();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to deactivate.";
      toast.error("Deactivate failed", message);
    } finally {
      setActing(null);
    }
  }

  async function handleActivate(manual: ClientBrandManual) {
    if (!effectiveToken) return;
    setActing(manual.id);
    try {
      await activateClientBrandManual(clientId, manual.id, effectiveToken);
      toast.success("Brand Manual", "Manual activated.");
      void reload();
      void loadHistory();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to activate. Another manual may already be active — deactivate it first.";
      toast.error("Activate failed", message);
    } finally {
      setActing(null);
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
          Brand Manual
        </span>
        <Button variant="outline" size="sm" onClick={openCreate}>
          <Plus className="h-3 w-3 mr-1" />
          New Manual
        </Button>
      </div>

      {loading ? (
        <p className="text-xs text-muted-foreground">Loading…</p>
      ) : active ? (
        <div className="rounded-md border bg-card p-3 space-y-2 max-h-80 overflow-y-auto">
          <div className="flex items-center justify-between">
            <span className="font-medium text-sm">{active.title}</span>
            <Badge variant="success" className="text-xs">
              Active
            </Badge>
          </div>

          {active.colorPalette.length > 0 && (
            <div className="flex items-center gap-1 flex-wrap">
              <Palette className="h-3 w-3 text-muted-foreground" />
              {active.colorPalette.slice(0, 8).map((c, i) => (
                <span
                  key={i}
                  title={`${c.name} ${c.hex}`}
                  className="h-4 w-4 rounded-full border border-border inline-block"
                  style={{ backgroundColor: c.hex }}
                />
              ))}
              {active.colorPalette.length > 8 && (
                <span className="text-xs text-muted-foreground">
                  +{active.colorPalette.length - 8}
                </span>
              )}
            </div>
          )}

          <div className="grid grid-cols-3 gap-2 text-xs text-muted-foreground">
            {active.logos.length > 0 && (
              <span className="flex items-center gap-1">
                <ImageIcon className="h-3 w-3" />
                {active.logos.length} logo{active.logos.length !== 1 ? "s" : ""}
              </span>
            )}
            {active.backgroundTemplates.length > 0 && (
              <span>{active.backgroundTemplates.length} background{active.backgroundTemplates.length !== 1 ? "s" : ""}</span>
            )}
            {active.typography.length > 0 && (
              <span className="flex items-center gap-1">
                <Type className="h-3 w-3" />
                {active.typography.length} font{active.typography.length !== 1 ? "s" : ""}
              </span>
            )}
          </div>

          {active.voiceAndTone && (
            <div className="text-xs text-muted-foreground">
              <span className="font-medium">Voice: </span>
              {truncate(active.voiceAndTone, 120)}
            </div>
          )}

          {active.notes && (
            <div className="text-xs text-muted-foreground">
              <span className="font-medium">Notes: </span>
              {truncate(active.notes, 120)}
            </div>
          )}

          <div className="flex gap-2 pt-1">
            <Button
              variant="outline"
              size="sm"
              onClick={() => openEdit(active)}
            >
              Edit
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => void handleDeactivate(active)}
              disabled={acting === active.id}
            >
              Deactivate
            </Button>
          </div>
        </div>
      ) : (
        <div className="rounded-md border border-dashed bg-muted/30 p-4 text-center space-y-2">
          <FileText className="h-5 w-5 mx-auto text-muted-foreground" />
          <p className="text-xs text-muted-foreground">No active brand manual.</p>
          <Button variant="outline" size="sm" onClick={openCreate}>
            <Plus className="h-3 w-3 mr-1" />
            New Manual
          </Button>
        </div>
      )}

      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground transition-colors"
        onClick={() => setShowHistory((v) => !v)}
      >
        <History className="h-3 w-3" />
        {showHistory ? "Hide history" : "Show history"}
      </Button>

      {showHistory && (
        <div className="space-y-1 max-h-48 overflow-y-auto">
          {history.length === 0 ? (
            <p className="text-xs text-muted-foreground">No inactive manuals.</p>
          ) : (
            history.map((m) => (
              <div
                key={m.id}
                className="flex items-center justify-between rounded border bg-muted/20 px-3 py-2"
              >
                <div>
                  <span className="text-sm">{m.title}</span>
                  <span className="text-xs text-muted-foreground ml-2">
                    {formatDate(m.updatedAtUtc)}
                  </span>
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => void handleActivate(m)}
                  disabled={acting === m.id}
                >
                  Activate
                </Button>
              </div>
            ))
          )}
        </div>
      )}

      {/* Create / Edit dialog */}
      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent className="max-h-[90vh] max-w-2xl overflow-hidden p-0">
          <DialogHeader className="px-6 pb-3 pr-12 pt-6">
            <DialogTitle>
              {dialogMode === "create" ? "New Brand Manual" : "Edit Brand Manual"}
            </DialogTitle>
            <DialogDescription>
              {dialogMode === "create"
                ? "Creating a new manual will auto-deactivate the current active one."
                : "Update the brand manual fields."}
            </DialogDescription>
          </DialogHeader>

          <form
            onSubmit={(e) => void handleSubmit(e)}
            className="max-h-[calc(90vh-8rem)] space-y-4 overflow-y-auto px-6 pb-6 pt-1"
            noValidate
          >
            {/* Title */}
            <div>
              <label className="text-xs font-medium">Title *</label>
              <Input
                value={form.title}
                onChange={(e) => {
                  setForm((prev) => ({ ...prev, title: e.target.value }));
                  setTitleError(null);
                }}
                maxLength={80}
                placeholder="Brand Manual 2026"
                aria-invalid={titleError ? "true" : "false"}
              />
              {titleError ? <p className="text-xs text-destructive">{titleError}</p> : null}
            </div>

            {/* Color Palette */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <label className="text-xs font-medium">Color Palette</label>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    setForm((prev) => ({
                      ...prev,
                      palette: [...prev.palette, { name: "", hex: "#000000", role: null, rank: 0 }],
                    }))
                  }
                >
                  <Plus className="h-3 w-3" /> Add Color
                </Button>
              </div>
              {form.palette.map((c, i) => (
                <ColorRow
                  key={i}
                  entry={c}
                  showValidationErrors={showValidationErrors}
                  onChange={(updated) =>
                    setForm((prev) => ({ ...prev, palette: updateAt(prev.palette, i, updated) }))
                  }
                  onRemove={() =>
                    setForm((prev) => ({ ...prev, palette: removeAt(prev.palette, i) }))
                  }
                />
              ))}
            </div>

            {/* Logos */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <label className="text-xs font-medium">Logos</label>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    setForm((prev) => ({
                      ...prev,
                      logos: [...prev.logos, { name: "", url: "", variant: null }],
                    }))
                  }
                >
                  <Plus className="h-3 w-3" /> Add Logo
                </Button>
              </div>
              {form.logos.map((l, i) => (
                <ImageAssetRow
                  key={i}
                  name={l.name}
                  url={l.url}
                  extra={l.variant}
                  extraPlaceholder="Variant"
                  assetLabel={`Logo ${l.name || i + 1}`}
                  showValidationErrors={showValidationErrors}
                  onChangeName={(v) =>
                    setForm((prev) => ({ ...prev, logos: updateAt(prev.logos, i, { ...prev.logos[i], name: v }) }))
                  }
                  onChangeUrl={(v) =>
                    setForm((prev) => ({ ...prev, logos: updateAt(prev.logos, i, { ...prev.logos[i], url: v }) }))
                  }
                  onChangeExtra={(v) =>
                    setForm((prev) => ({ ...prev, logos: updateAt(prev.logos, i, { ...prev.logos[i], variant: v }) }))
                  }
                  onUpload={handleImageUpload}
                  onRemove={() =>
                    setForm((prev) => ({ ...prev, logos: removeAt(prev.logos, i) }))
                  }
                />
              ))}
            </div>

            {/* Typography */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <label className="text-xs font-medium">Typography</label>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setFontPickerOpen((open) => !open)}
                >
                  {fontPickerOpen ? (
                    <>
                      <X className="h-3 w-3" /> Close
                    </>
                  ) : (
                    <>
                      <Plus className="h-3 w-3" /> Add Font
                    </>
                  )}
                </Button>
              </div>

              {form.typo.length > 0 ? (
                <div className="grid gap-2 sm:grid-cols-2">
                  {form.typo.map((entry, index) => (
                    <SelectedFontCard
                      key={`${entry.fontFamily}-${index}`}
                      entry={entry}
                      onRemove={() =>
                        setForm((prev) => ({ ...prev, typo: removeAt(prev.typo, index) }))
                      }
                    />
                  ))}
                </div>
              ) : null}

              {fontPickerOpen ? (
                <div className="space-y-3 rounded-md border border-border bg-background p-3 shadow-sm">
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-medium text-muted-foreground">Available fonts</span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="h-8 w-8 p-0"
                      onClick={() => setFontPickerOpen(false)}
                      aria-label="Close font picker"
                    >
                      <X className="h-4 w-4" />
                    </Button>
                  </div>
                  <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
                    {COMMON_FONT_FAMILIES.map((fontFamily) => {
                      const selected = form.typo.some(
                        (e) => e.fontFamily.trim().toLowerCase() === fontFamily.toLowerCase(),
                      );

                      return (
                        <Button
                          key={fontFamily}
                          type="button"
                          variant={selected ? "secondary" : "outline"}
                          aria-pressed={selected}
                          onClick={() => addFont(fontFamily)}
                          className={cn(
                            "h-auto min-h-16 justify-start px-3 py-2 text-left",
                            selected && "border-primary/60 bg-primary/10",
                          )}
                        >
                          <span className="flex min-w-0 flex-col items-start">
                            <span
                              className="max-w-full truncate text-base leading-tight text-foreground"
                              style={{ fontFamily: getFontPreviewStack(fontFamily) }}
                            >
                              Aa Brand voice
                            </span>
                            <span className="mt-1 max-w-full truncate text-xs font-normal text-muted-foreground">
                              {fontFamily}
                            </span>
                          </span>
                        </Button>
                      );
                    })}
                  </div>

                  <div className="grid gap-2 rounded-md border border-dashed border-border/80 p-3 sm:grid-cols-[1fr_auto]">
                    <div className="space-y-1">
                      <Input
                        value={customFontFamily}
                        onChange={(event) => setCustomFontFamily(event.target.value)}
                        placeholder="Custom font family"
                      />
                      {customFontFamily.trim() ? (
                        <p
                          className="truncate text-base text-foreground"
                          style={{ fontFamily: getFontPreviewStack(customFontFamily) }}
                        >
                          Aa {customFontFamily.trim()}
                        </p>
                      ) : null}
                    </div>
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => addFont(customFontFamily)}
                      disabled={!customFontFamily.trim()}
                    >
                      Add Custom
                    </Button>
                  </div>
                </div>
              ) : null}

              {showValidationErrors &&
              form.typo.some((entry) => !entry.fontFamily.trim()) ? (
                <p className="text-xs text-destructive">
                  Remove empty font entries before saving.
                </p>
              ) : null}
            </div>

            {/* Backgrounds */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <label className="text-xs font-medium">Background Templates</label>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    setForm((prev) => ({
                      ...prev,
                      bg: [...prev.bg, { name: "", url: "", useCase: null }],
                    }))
                  }
                >
                  <Plus className="h-3 w-3" /> Add Background
                </Button>
              </div>
              {form.bg.map((b, i) => (
                <ImageAssetRow
                  key={i}
                  name={b.name}
                  url={b.url}
                  extra={b.useCase}
                  extraPlaceholder="Use Case"
                  assetLabel={`Background ${b.name || i + 1}`}
                  showValidationErrors={showValidationErrors}
                  onChangeName={(v) =>
                    setForm((prev) => ({ ...prev, bg: updateAt(prev.bg, i, { ...prev.bg[i], name: v }) }))
                  }
                  onChangeUrl={(v) =>
                    setForm((prev) => ({ ...prev, bg: updateAt(prev.bg, i, { ...prev.bg[i], url: v }) }))
                  }
                  onChangeExtra={(v) =>
                    setForm((prev) => ({ ...prev, bg: updateAt(prev.bg, i, { ...prev.bg[i], useCase: v }) }))
                  }
                  onUpload={handleImageUpload}
                  onRemove={() =>
                    setForm((prev) => ({ ...prev, bg: removeAt(prev.bg, i) }))
                  }
                />
              ))}
            </div>

            {/* Voice and Tone */}
            <div>
              <label className="text-xs font-medium">Voice &amp; Tone</label>
              <textarea
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 min-h-20 resize-y"
                value={form.voice}
                onChange={(e) => setForm((prev) => ({ ...prev, voice: e.target.value }))}
                maxLength={2000}
                placeholder="Describe the brand voice and tone…"
              />
            </div>

            {/* Notes */}
            <div>
              <label className="text-xs font-medium">Notes</label>
              <textarea
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 min-h-20 resize-y"
                value={form.notes}
                onChange={(e) => setForm((prev) => ({ ...prev, notes: e.target.value }))}
                maxLength={4000}
                placeholder="Internal guidelines, brand notes…"
              />
            </div>

            {signalError && (
              <p className="text-sm text-destructive">{signalError}</p>
            )}

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => setDialogOpen(false)}
              >
                Cancel
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? "Saving…" : dialogMode === "create" ? "Create" : "Save"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
