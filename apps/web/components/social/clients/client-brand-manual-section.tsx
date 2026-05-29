"use client";

import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import type { PointerEvent as ReactPointerEvent } from "react";
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

function clamp(value: number, min = 0, max = 1): number {
  return Math.min(max, Math.max(min, value));
}

function expandHex(hex: string): string {
  const normalized = hex.trim().replace("#", "");
  if (normalized.length === 3) {
    return `#${normalized
      .split("")
      .map((char) => char + char)
      .join("")}`;
  }

  return `#${normalized.slice(0, 6)}`;
}

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const safeHex = validateHex(hex) ? "#000000" : expandHex(hex);
  const value = Number.parseInt(safeHex.slice(1), 16);
  return {
    r: (value >> 16) & 255,
    g: (value >> 8) & 255,
    b: value & 255,
  };
}

function rgbToHex(r: number, g: number, b: number): string {
  return `#${[r, g, b]
    .map((value) => Math.round(value).toString(16).padStart(2, "0"))
    .join("")}`;
}

function rgbToHsv(r: number, g: number, b: number): { h: number; s: number; v: number } {
  const red = r / 255;
  const green = g / 255;
  const blue = b / 255;
  const max = Math.max(red, green, blue);
  const min = Math.min(red, green, blue);
  const delta = max - min;
  let h = 0;

  if (delta !== 0) {
    if (max === red) h = 60 * (((green - blue) / delta) % 6);
    if (max === green) h = 60 * ((blue - red) / delta + 2);
    if (max === blue) h = 60 * ((red - green) / delta + 4);
  }

  return {
    h: h < 0 ? h + 360 : h,
    s: max === 0 ? 0 : delta / max,
    v: max,
  };
}

function hsvToHex(h: number, s: number, v: number): string {
  const c = v * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = v - c;
  let r = 0;
  let g = 0;
  let b = 0;

  if (h < 60) [r, g, b] = [c, x, 0];
  else if (h < 120) [r, g, b] = [x, c, 0];
  else if (h < 180) [r, g, b] = [0, c, x];
  else if (h < 240) [r, g, b] = [0, x, c];
  else if (h < 300) [r, g, b] = [x, 0, c];
  else [r, g, b] = [c, 0, x];

  return rgbToHex((r + m) * 255, (g + m) * 255, (b + m) * 255);
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

// --- Row editors ---

function toPickerValue(hex: string): string {
  const h = hex.trim();
  if (/^#[0-9a-fA-F]{3}$/.test(h)) {
    return `#${h[1]}${h[1]}${h[2]}${h[2]}${h[3]}${h[3]}`;
  }
  if (/^#[0-9a-fA-F]{6}$/.test(h)) return h;
  if (/^#[0-9a-fA-F]{8}$/.test(h)) return h.slice(0, 7);
  return "#000000";
}

function ColorPickerPanel({
  value,
  onChange,
  onClose,
}: {
  value: string;
  onChange: (value: string) => void;
  onClose: () => void;
}) {
  const planeRef = useRef<HTMLDivElement>(null);
  const pickerValue = toPickerValue(value);
  const { r, g, b } = hexToRgb(pickerValue);
  const hsv = rgbToHsv(r, g, b);

  function updateFromPointer(event: ReactPointerEvent<HTMLDivElement>) {
    const rect = planeRef.current?.getBoundingClientRect();
    if (!rect) return;

    const s = clamp((event.clientX - rect.left) / rect.width);
    const v = 1 - clamp((event.clientY - rect.top) / rect.height);
    onChange(hsvToHex(hsv.h, s, v));
  }

  return (
    <div className="rounded-md border border-border bg-background p-3 shadow-sm">
      <div className="mb-2 flex items-center justify-between gap-2">
        <span className="text-xs font-medium text-muted-foreground">Color picker</span>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-8 w-8 p-0"
          onClick={onClose}
          aria-label="Close color picker"
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      <div
        ref={planeRef}
        className="relative h-40 cursor-crosshair overflow-hidden rounded-md border border-border"
        style={{ backgroundColor: `hsl(${hsv.h}, 100%, 50%)` }}
        onPointerDown={(event) => {
          event.currentTarget.setPointerCapture(event.pointerId);
          updateFromPointer(event);
        }}
        onPointerMove={(event) => {
          if (event.buttons === 1) updateFromPointer(event);
        }}
      >
        <div className="absolute inset-0 bg-gradient-to-r from-white to-transparent" />
        <div className="absolute inset-0 bg-gradient-to-t from-black to-transparent" />
        <span
          className="absolute h-4 w-4 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white shadow-[0_0_0_1px_rgba(0,0,0,0.45)]"
          style={{
            left: `${hsv.s * 100}%`,
            top: `${(1 - hsv.v) * 100}%`,
          }}
        />
      </div>

      <input
        type="range"
        min={0}
        max={360}
        value={Math.round(hsv.h)}
        onChange={(event) => onChange(hsvToHex(Number(event.target.value), hsv.s, hsv.v))}
        className="mt-3 w-full cursor-pointer accent-primary"
        style={{
          background:
            "linear-gradient(90deg, #ff0000, #ffff00, #00ff00, #00ffff, #0000ff, #ff00ff, #ff0000)",
        }}
        aria-label="Hue"
      />

      <div className="mt-3 grid grid-cols-8 gap-1.5">
        {COLOR_SWATCHES.map((swatch) => (
          <Button
            key={swatch}
            type="button"
            variant="outline"
            size="sm"
            className="h-8 w-8 p-0"
            style={{ backgroundColor: swatch }}
            onClick={() => onChange(swatch)}
            aria-label={`Use ${swatch}`}
          />
        ))}
      </div>
    </div>
  );
}

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
              style={{ backgroundColor: toPickerValue(entry.hex) }}
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
        <Input
          placeholder="Role (optional)"
          value={entry.role ?? ""}
          onChange={(e) => onChange({ ...entry, role: e.target.value || null })}
          className="w-28"
        />
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
        <ColorPickerPanel
          value={entry.hex}
          onChange={(hex) => onChange({ ...entry, hex })}
          onClose={() => setPickerOpen(false)}
        />
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

  // Form state
  const [fTitle, setFTitle] = useState("");
  const [fPalette, setFPalette] = useState<ColorEntry[]>([]);
  const [fLogos, setFLogos] = useState<LogoEntry[]>([]);
  const [fBg, setFBg] = useState<BackgroundEntry[]>([]);
  const [fTypo, setFTypo] = useState<TypographyEntry[]>([]);
  const [fVoice, setFVoice] = useState("");
  const [fNotes, setFNotes] = useState("");

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
    setFTitle("");
    setFPalette([]);
    setFLogos([]);
    setFBg([]);
    setFTypo([]);
    setFVoice("");
    setFNotes("");
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
    setFTitle(manual.title);
    setFPalette([...manual.colorPalette]);
    setFLogos([...manual.logos]);
    setFBg([...manual.backgroundTemplates]);
    setFTypo([...manual.typography]);
    setFVoice(manual.voiceAndTone ?? "");
    setFNotes(manual.notes ?? "");
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
    if (fTypo.some((entry) => entry.fontFamily.trim().toLowerCase() === trimmed.toLowerCase())) {
      return;
    }

    setFTypo([...fTypo, createTypographyEntry(trimmed)]);
    setCustomFontFamily("");
    setSignalError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    e.stopPropagation();
    setShowValidationErrors(true);

    const nextTitleError = fTitle.trim() ? null : "Title is required.";
    setTitleError(nextTitleError);

    if (!hasSignal(fPalette, fLogos, fBg, fTypo, fVoice, fNotes)) {
      setSignalError(
        "At least one field is required (palette, logos, backgrounds, typography, voice, or notes).",
      );
      return;
    }

    if (hasInvalidStructuredEntries(fPalette, fLogos, fBg, fTypo)) {
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
        title: fTitle.trim(),
        colorPalette: fPalette,
        logos: fLogos,
        backgroundTemplates: fBg,
        typography: normalizeTypographyEntries(fTypo),
        voiceAndTone: fVoice.trim() || undefined,
        notes: fNotes.trim() || undefined,
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
                    {new Date(m.updatedAtUtc).toLocaleDateString()}
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
                value={fTitle}
                onChange={(e) => {
                  setFTitle(e.target.value);
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
                    setFPalette([...fPalette, { name: "", hex: "#000000", role: null }])
                  }
                >
                  <Plus className="h-3 w-3" /> Add Color
                </Button>
              </div>
              {fPalette.map((c, i) => (
                <ColorRow
                  key={i}
                  entry={c}
                  showValidationErrors={showValidationErrors}
                  onChange={(updated) => {
                    const copy = [...fPalette];
                    copy[i] = updated;
                    setFPalette(copy);
                  }}
                  onRemove={() => setFPalette(fPalette.filter((_, idx) => idx !== i))}
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
                    setFLogos([...fLogos, { name: "", url: "", variant: null }])
                  }
                >
                  <Plus className="h-3 w-3" /> Add Logo
                </Button>
              </div>
              {fLogos.map((l, i) => (
                <ImageAssetRow
                  key={i}
                  name={l.name}
                  url={l.url}
                  extra={l.variant}
                  extraPlaceholder="Variant"
                  assetLabel={`Logo ${l.name || i + 1}`}
                  showValidationErrors={showValidationErrors}
                  onChangeName={(v) => {
                    const copy = [...fLogos];
                    copy[i] = { ...copy[i], name: v };
                    setFLogos(copy);
                  }}
                  onChangeUrl={(v) => {
                    const copy = [...fLogos];
                    copy[i] = { ...copy[i], url: v };
                    setFLogos(copy);
                  }}
                  onChangeExtra={(v) => {
                    const copy = [...fLogos];
                    copy[i] = { ...copy[i], variant: v };
                    setFLogos(copy);
                  }}
                  onUpload={handleImageUpload}
                  onRemove={() => setFLogos(fLogos.filter((_, idx) => idx !== i))}
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

              {fTypo.length > 0 ? (
                <div className="grid gap-2 sm:grid-cols-2">
                  {fTypo.map((entry, index) => (
                    <SelectedFontCard
                      key={`${entry.fontFamily}-${index}`}
                      entry={entry}
                      onRemove={() => setFTypo(fTypo.filter((_, idx) => idx !== index))}
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
                      const selected = fTypo.some(
                        (entry) =>
                          entry.fontFamily.trim().toLowerCase() === fontFamily.toLowerCase(),
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
              fTypo.some((entry) => !entry.fontFamily.trim()) ? (
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
                    setFBg([...fBg, { name: "", url: "", useCase: null }])
                  }
                >
                  <Plus className="h-3 w-3" /> Add Background
                </Button>
              </div>
              {fBg.map((b, i) => (
                <ImageAssetRow
                  key={i}
                  name={b.name}
                  url={b.url}
                  extra={b.useCase}
                  extraPlaceholder="Use Case"
                  assetLabel={`Background ${b.name || i + 1}`}
                  showValidationErrors={showValidationErrors}
                  onChangeName={(v) => {
                    const copy = [...fBg];
                    copy[i] = { ...copy[i], name: v };
                    setFBg(copy);
                  }}
                  onChangeUrl={(v) => {
                    const copy = [...fBg];
                    copy[i] = { ...copy[i], url: v };
                    setFBg(copy);
                  }}
                  onChangeExtra={(v) => {
                    const copy = [...fBg];
                    copy[i] = { ...copy[i], useCase: v };
                    setFBg(copy);
                  }}
                  onUpload={handleImageUpload}
                  onRemove={() => setFBg(fBg.filter((_, idx) => idx !== i))}
                />
              ))}
            </div>

            {/* Voice and Tone */}
            <div>
              <label className="text-xs font-medium">Voice &amp; Tone</label>
              <textarea
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 min-h-20 resize-y"
                value={fVoice}
                onChange={(e) => setFVoice(e.target.value)}
                maxLength={2000}
                placeholder="Describe the brand voice and tone…"
              />
            </div>

            {/* Notes */}
            <div>
              <label className="text-xs font-medium">Notes</label>
              <textarea
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 min-h-20 resize-y"
                value={fNotes}
                onChange={(e) => setFNotes(e.target.value)}
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
