"use client";

// ImputePanel — PA-20, PA-21, PA-22
// Presents the 11 actionable AI-detected fields grouped by destination ABM section.
// Shown ONLY when status === "Analyzed" AND clientId is non-null (PA-22).
// Each target shows a PREVIEW of the detected value; disabled with "sin dato detectado"
// when the underlying AI value is null/empty.
// Includes select-all / clear helpers and opens a ConfirmationDialog before writing.

import { useState, useCallback, useMemo } from "react";
import { Download, CheckSquare, Square } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Button } from "@/components/ui/button";
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog";
import { useToast } from "@/components/ui/toaster";
import { imputeProfileAnalysis } from "@/lib/api/profile-analyzer";
import type { ProfileImputeTarget, ProfileAiAnalysisDto } from "@/lib/api/types/profile-analyzer";

// ── Target descriptors ────────────────────────────────────────────────────────

/**
 * Static metadata for each of the 11 impute targets.
 * `group` maps to an ABM section label; `label` is the human-readable field name;
 * `getPreview` extracts the detected value string from the AI analysis.
 */
interface TargetDescriptor {
  target: ProfileImputeTarget;
  label: string;
  group: "Manual de marca" | "Plan de contenido" | "Estrategia general";
  getPreview: (ai: ProfileAiAnalysisDto) => string | null;
}

const TARGET_DESCRIPTORS: TargetDescriptor[] = [
  // ── Manual de marca ──────────────────────────────────────────────────────────
  {
    target: "ColorPalette",
    label: "Paleta de colores",
    group: "Manual de marca",
    getPreview: (ai) =>
      ai.detectedBrandColors.length > 0
        ? ai.detectedBrandColors.map((c) => `${c.name} (${c.hex})`).join(", ")
        : null,
  },
  {
    target: "Typography",
    label: "Tipografías",
    group: "Manual de marca",
    getPreview: (ai) =>
      ai.detectedFonts.length > 0 ? ai.detectedFonts.join(", ") : null,
  },
  {
    target: "BackgroundTemplates",
    label: "Imágenes de fondo",
    group: "Manual de marca",
    getPreview: (ai) =>
      ai.detectedBackgroundImages.length > 0
        ? ai.detectedBackgroundImages.join(", ")
        : null,
  },
  {
    target: "VoiceAndTone",
    label: "Voz y tono",
    group: "Manual de marca",
    getPreview: (ai) => ai.voiceTone ?? null,
  },
  // ── Plan de contenido ─────────────────────────────────────────────────────────
  {
    target: "ObjectiveText",
    label: "Objetivo mensual",
    group: "Plan de contenido",
    getPreview: (ai) => ai.monthlyObjectiveSuggestion ?? null,
  },
  {
    target: "ObjectiveKpis",
    label: "KPIs",
    group: "Plan de contenido",
    getPreview: (ai) =>
      ai.recommendedKpis.length > 0 ? ai.recommendedKpis.join(", ") : null,
  },
  {
    target: "ContentStrategyText",
    label: "Estrategia de contenido",
    group: "Plan de contenido",
    getPreview: (ai) => ai.contentStrategySuggestion ?? null,
  },
  // ── Estrategia general ────────────────────────────────────────────────────────
  {
    target: "ContentPillars",
    label: "Pilares de contenido",
    group: "Estrategia general",
    getPreview: (ai) =>
      ai.contentPillars.length > 0 ? ai.contentPillars.join(", ") : null,
  },
  {
    target: "ToneKeywords",
    label: "Keywords de tono",
    group: "Estrategia general",
    getPreview: (ai) =>
      // voiceTone is also the source for tone keywords (split by comma/slash in backend)
      ai.voiceTone ?? null,
  },
  {
    target: "TargetAudience",
    label: "Audiencia objetivo",
    group: "Estrategia general",
    getPreview: (ai) => ai.audienceProfile ?? null,
  },
  {
    target: "PostingFrequency",
    label: "Frecuencia de publicación",
    group: "Estrategia general",
    getPreview: (ai) =>
      ai.postingDays.length > 0 ? ai.postingDays.join(", ") : null,
  },
];

const GROUPS: Array<"Manual de marca" | "Plan de contenido" | "Estrategia general"> = [
  "Manual de marca",
  "Plan de contenido",
  "Estrategia general",
];

// ── Helpers ───────────────────────────────────────────────────────────────────

function truncatePreview(value: string, maxChars = 80): string {
  if (value.length <= maxChars) return value;
  return value.slice(0, maxChars) + "…";
}

// ── Types ─────────────────────────────────────────────────────────────────────

export interface ImputePanelProps {
  analysisId: string;
  clientId: string;
  aiAnalysis: ProfileAiAnalysisDto;
  token: string | undefined;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ImputePanel({ analysisId, clientId, aiAnalysis, token }: ImputePanelProps) {
  const toast = useToast();

  // Which targets are currently checked
  const [selected, setSelected] = useState<Set<ProfileImputeTarget>>(new Set());

  // Dialog + loading state
  const [dialogOpen, setDialogOpen] = useState(false);
  const [isImputing, setIsImputing] = useState(false);
  const [lastImputedTargets, setLastImputedTargets] = useState<ProfileImputeTarget[] | null>(null);

  // Pre-compute which targets have a detectable value from the AI analysis
  const previews = useMemo<Map<ProfileImputeTarget, string | null>>(
    () => new Map(TARGET_DESCRIPTORS.map((d) => [d.target, d.getPreview(aiAnalysis)])),
    [aiAnalysis]
  );

  // Targets that have a value — only those can be selected
  const availableTargets = useMemo<Set<ProfileImputeTarget>>(
    () => new Set(TARGET_DESCRIPTORS.filter((d) => previews.get(d.target) !== null).map((d) => d.target)),
    [previews]
  );

  // ── Selection handlers ──────────────────────────────────────────────────────

  const toggleTarget = useCallback(
    (target: ProfileImputeTarget) => {
      if (!availableTargets.has(target)) return; // guard: disabled targets cannot be toggled
      setSelected((prev) => {
        const next = new Set(prev);
        if (next.has(target)) {
          next.delete(target);
        } else {
          next.add(target);
        }
        return next;
      });
    },
    [availableTargets]
  );

  const selectAll = useCallback(() => {
    setSelected(new Set(availableTargets));
  }, [availableTargets]);

  const clearAll = useCallback(() => {
    setSelected(new Set());
  }, []);

  // ── Impute flow ─────────────────────────────────────────────────────────────

  const handleImputeClick = useCallback(() => {
    if (selected.size === 0) return;
    setDialogOpen(true);
  }, [selected]);

  const handleConfirm = useCallback(async () => {
    const targets = Array.from(selected);
    setDialogOpen(false);
    setIsImputing(true);
    try {
      await imputeProfileAnalysis(analysisId, { targets }, token);
      setLastImputedTargets(targets);
      toast.success(
        "Campos imputados correctamente",
        `Se aplicaron ${targets.length} campo${targets.length !== 1 ? "s" : ""} al cliente.`
      );
      // Clear selection after successful impute
      setSelected(new Set());
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "message" in err
          ? String((err as { message: unknown }).message)
          : "No se pudo imputar. Verificá el estado del análisis y que el cliente esté vinculado.";
      toast.error("Error al imputar", msg);
    } finally {
      setIsImputing(false);
    }
  }, [analysisId, selected, token, toast]);

  const handleCancel = useCallback(() => {
    setDialogOpen(false);
  }, []);

  // ── Confirmation dialog content ─────────────────────────────────────────────

  const selectedDescriptors = TARGET_DESCRIPTORS.filter((d) => selected.has(d.target));

  const confirmDescription =
    selected.size === 0
      ? ""
      : `Se escribirán los siguientes ${selected.size} campo${selected.size !== 1 ? "s" : ""} al ABM del cliente (ID: ${clientId}):\n${selectedDescriptors.map((d) => `• ${d.label}`).join("\n")}`;

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <>
      <Card className="border-border/40 bg-background/60">
        <CardHeader className="pb-3">
          <div className="flex items-center justify-between gap-3">
            <CardTitle className="text-sm flex items-center gap-2">
              <Download className="h-4 w-4 text-muted-foreground/60" />
              Imputar al cliente
            </CardTitle>
            {/* Select-all / clear helpers */}
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={selectAll}
                disabled={availableTargets.size === 0}
                className="flex items-center gap-1 text-xs text-muted-foreground/70 hover:text-foreground transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <CheckSquare className="h-3.5 w-3.5" />
                Seleccionar todos
              </button>
              <span className="text-border/60 text-xs">|</span>
              <button
                type="button"
                onClick={clearAll}
                disabled={selected.size === 0}
                className="flex items-center gap-1 text-xs text-muted-foreground/70 hover:text-foreground transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <Square className="h-3.5 w-3.5" />
                Limpiar
              </button>
            </div>
          </div>
        </CardHeader>

        <CardContent className="pt-0 space-y-5">
          {/* Target groups */}
          {GROUPS.map((group) => {
            const groupTargets = TARGET_DESCRIPTORS.filter((d) => d.group === group);
            return (
              <div key={group} className="space-y-2">
                <p className="text-xs font-semibold text-muted-foreground/80 uppercase tracking-wide">
                  {group}
                </p>
                <div className="space-y-2">
                  {groupTargets.map((descriptor) => {
                    const preview = previews.get(descriptor.target) ?? null;
                    const isAvailable = preview !== null;
                    const isChecked = selected.has(descriptor.target);

                    return (
                      <label
                        key={descriptor.target}
                        className={[
                          "flex items-start gap-3 rounded-md border px-3 py-2.5 transition-colors",
                          isAvailable
                            ? "cursor-pointer border-border/40 hover:border-primary/40 hover:bg-primary/5"
                            : "cursor-not-allowed border-border/20 bg-muted/20 opacity-60",
                          isChecked ? "border-primary/50 bg-primary/5" : "",
                        ]
                          .filter(Boolean)
                          .join(" ")}
                      >
                        <Checkbox
                          checked={isChecked}
                          disabled={!isAvailable}
                          onChange={() => toggleTarget(descriptor.target)}
                          className="mt-0.5 shrink-0"
                          aria-label={descriptor.label}
                        />
                        <div className="min-w-0 flex-1">
                          <p className="text-xs font-medium text-foreground/90">{descriptor.label}</p>
                          {isAvailable ? (
                            <p className="mt-0.5 text-xs text-muted-foreground/70 break-words">
                              {truncatePreview(preview!)}
                            </p>
                          ) : (
                            <p className="mt-0.5 text-xs text-muted-foreground/40 italic">
                              sin dato detectado
                            </p>
                          )}
                        </div>
                      </label>
                    );
                  })}
                </div>
              </div>
            );
          })}

          {/* Action row */}
          <div className="flex items-center justify-between gap-3 pt-1">
            <p className="text-xs text-muted-foreground/60">
              {selected.size > 0
                ? `${selected.size} campo${selected.size !== 1 ? "s" : ""} seleccionado${selected.size !== 1 ? "s" : ""}`
                : "Seleccioná los campos a imputar"}
            </p>
            <Button
              size="sm"
              disabled={selected.size === 0 || isImputing}
              onClick={handleImputeClick}
              className="text-xs"
            >
              {isImputing ? "Imputando…" : "Imputar seleccionados al cliente"}
            </Button>
          </div>

          {/* Success summary — shown after last impute */}
          {lastImputedTargets !== null && lastImputedTargets.length > 0 && (
            <div className="rounded-md border border-emerald-500/30 bg-emerald-500/5 px-3 py-2">
              <p className="text-xs font-medium text-emerald-700 dark:text-emerald-300">
                Última imputación aplicada
              </p>
              <p className="mt-0.5 text-xs text-emerald-800/80 dark:text-emerald-200/80">
                {lastImputedTargets
                  .map(
                    (t) => TARGET_DESCRIPTORS.find((d) => d.target === t)?.label ?? t
                  )
                  .join(", ")}
              </p>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Confirmation dialog — opened before writing to client ABM */}
      <ConfirmationDialog
        open={dialogOpen}
        title="Confirmar imputación al cliente"
        description={confirmDescription}
        confirmLabel="Confirmar y aplicar"
        cancelLabel="Cancelar"
        confirmVariant="default"
        disableDismiss={isImputing}
        onConfirm={() => void handleConfirm()}
        onCancel={handleCancel}
      />
    </>
  );
}
