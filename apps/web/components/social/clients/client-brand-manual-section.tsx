"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import { Plus, History, Palette, Image, Type, FileText } from "lucide-react";
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

const HEX_REGEX = /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/;

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

// --- Row editors ---

/** Normalize any valid hex variant to #RRGGBB for <input type="color"> */
function toPickerValue(hex: string): string {
  const h = hex.trim();
  if (/^#[0-9a-fA-F]{3}$/.test(h)) {
    return `#${h[1]}${h[1]}${h[2]}${h[2]}${h[3]}${h[3]}`;
  }
  if (/^#[0-9a-fA-F]{6}$/.test(h)) return h;
  if (/^#[0-9a-fA-F]{8}$/.test(h)) return h.slice(0, 7);
  return "#000000";
}

function ColorRow({
  entry,
  onChange,
  onRemove,
}: {
  entry: ColorEntry;
  onChange: (e: ColorEntry) => void;
  onRemove: () => void;
}) {
  const hexError = entry.hex ? validateHex(entry.hex) : null;
  return (
    <div className="flex gap-2 items-start">
      <Input
        placeholder="Name"
        value={entry.name}
        onChange={(e) => onChange({ ...entry, name: e.target.value })}
        className="flex-1"
      />
      <div className="flex flex-col gap-1">
        <div className="flex items-center gap-1">
          <input
            type="color"
            value={toPickerValue(entry.hex)}
            onChange={(e) => onChange({ ...entry, hex: e.target.value })}
            className="h-9 w-9 shrink-0 cursor-pointer rounded border border-input bg-background p-0.5"
            title="Pick a color"
          />
          <Input
            placeholder="#FF0000"
            value={entry.hex}
            onChange={(e) => onChange({ ...entry, hex: e.target.value })}
            className="w-28"
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
      <Button type="button" variant="outline" size="sm" onClick={onRemove}>
        ×
      </Button>
    </div>
  );
}

function ImageAssetRow({
  name,
  url,
  extra,
  extraPlaceholder,
  assetLabel,
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
  onChangeName: (v: string) => void;
  onChangeUrl: (v: string) => void;
  onChangeExtra: (v: string | null) => void;
  onUpload: (file: File) => Promise<string>;
  onRemove: () => void;
}) {
  return (
    <div className="grid gap-2 rounded-md border border-border/60 bg-muted/20 p-3">
      <div className="flex gap-2 items-start flex-wrap">
        <Input
          placeholder="Name"
          value={name}
          onChange={(e) => onChangeName(e.target.value)}
          className="flex-1 min-w-24"
        />
        <Input
          placeholder={extraPlaceholder}
          value={extra ?? ""}
          onChange={(e) => onChangeExtra(e.target.value || null)}
          className="w-32"
        />
        <Button type="button" variant="outline" size="sm" onClick={onRemove}>
          ×
        </Button>
      </div>
      <ImageUploadField
        value={url}
        onChange={onChangeUrl}
        onUpload={onUpload}
        previewAlt={assetLabel}
      />
    </div>
  );
}

function TypoRow({
  entry,
  onChange,
  onRemove,
}: {
  entry: TypographyEntry;
  onChange: (e: TypographyEntry) => void;
  onRemove: () => void;
}) {
  const fontUrlError = entry.fontUrl ? validateHttpUrl(entry.fontUrl) : null;
  return (
    <div className="flex gap-2 items-start flex-wrap">
      <Input
        placeholder="Name"
        value={entry.name}
        onChange={(e) => onChange({ ...entry, name: e.target.value })}
        className="flex-1 min-w-24"
      />
      <Input
        placeholder="Font Family"
        value={entry.fontFamily}
        onChange={(e) => onChange({ ...entry, fontFamily: e.target.value })}
        className="flex-1 min-w-28"
      />
      <Input
        placeholder="Role"
        value={entry.role ?? ""}
        onChange={(e) => onChange({ ...entry, role: e.target.value || null })}
        className="w-24"
      />
      <Input
        placeholder="Weight"
        value={entry.weight ?? ""}
        onChange={(e) => onChange({ ...entry, weight: e.target.value || null })}
        className="w-20"
      />
      <div className="flex flex-col gap-1 flex-1 min-w-32">
        <Input
          placeholder="Font URL (optional)"
          value={entry.fontUrl ?? ""}
          onChange={(e) =>
            onChange({ ...entry, fontUrl: e.target.value || null })
          }
        />
        {fontUrlError && (
          <p className="text-xs text-destructive">{fontUrlError}</p>
        )}
      </div>
      <Button type="button" variant="outline" size="sm" onClick={onRemove}>
        ×
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
  const [signalError, setSignalError] = useState<string | null>(null);

  // Form state
  const [fTitle, setFTitle] = useState("");
  const [fPalette, setFPalette] = useState<ColorEntry[]>([]);
  const [fLogos, setFLogos] = useState<LogoEntry[]>([]);
  const [fBg, setFBg] = useState<BackgroundEntry[]>([]);
  const [fTypo, setFTypo] = useState<TypographyEntry[]>([]);
  const [fVoice, setFVoice] = useState("");
  const [fNotes, setFNotes] = useState("");

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
    setSignalError(null);
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
    setSignalError(null);
    setDialogOpen(true);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!hasSignal(fPalette, fLogos, fBg, fTypo, fVoice, fNotes)) {
      setSignalError(
        "At least one field is required (palette, logos, backgrounds, typography, voice, or notes).",
      );
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
        typography: fTypo,
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
            <Badge variant="outline" className="text-xs text-green-600 border-green-300">
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
                <Image className="h-3 w-3" />
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

      <button
        type="button"
        className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground transition-colors"
        onClick={() => setShowHistory((v) => !v)}
      >
        <History className="h-3 w-3" />
        {showHistory ? "Hide history" : "Show history"}
      </button>

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
        <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>
              {dialogMode === "create" ? "New Brand Manual" : "Edit Brand Manual"}
            </DialogTitle>
            <DialogDescription>
              {dialogMode === "create"
                ? "Creating a new manual will auto-deactivate the current active one."
                : "Update the brand manual fields."}
            </DialogDescription>
          </DialogHeader>

          <form onSubmit={(e) => void handleSubmit(e)} className="space-y-4">
            {/* Title */}
            <div>
              <label className="text-xs font-medium">Title *</label>
              <Input
                required
                value={fTitle}
                onChange={(e) => setFTitle(e.target.value)}
                maxLength={80}
                placeholder="Brand Manual 2026"
              />
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

            {/* Typography */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <label className="text-xs font-medium">Typography</label>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    setFTypo([
                      ...fTypo,
                      { name: "", fontFamily: "", role: null, fontUrl: null, weight: null },
                    ])
                  }
                >
                  <Plus className="h-3 w-3" /> Add Font
                </Button>
              </div>
              {fTypo.map((t, i) => (
                <TypoRow
                  key={i}
                  entry={t}
                  onChange={(updated) => {
                    const copy = [...fTypo];
                    copy[i] = updated;
                    setFTypo(copy);
                  }}
                  onRemove={() => setFTypo(fTypo.filter((_, idx) => idx !== i))}
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
