"use client";

// ProfileAnalyzerModule — "Mundo 2" lazy module boundary.
// recharts is imported ONLY inside this module so Mundo 1 bundle is unaffected (Design Decision 7).
// This file is the React.lazy target from page.tsx (see AnalyzerPageWithModeSwitch).

import { useState, useCallback } from "react";
import { History, Trash2, RefreshCw } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ProfileSourcePicker } from "@/components/social/profile-analyzer/ProfileSourcePicker";
import { AnalysisProgress } from "@/components/social/profile-analyzer/AnalysisProgress";
import { useAccessToken } from "@/components/auth/use-access-token";
import {
  analyzeProfile,
  listProfileAnalyses,
  deleteProfileAnalysis,
} from "@/lib/api/profile-analyzer";
import type {
  ProfileAnalysisStatusResponse,
  ProfileAnalysisHistoryItemDto,
  ProfileAnalysisStatus,
} from "@/lib/api/types/profile-analyzer";
import type { ProfileSourcePickerSubmitPayload } from "@/components/social/profile-analyzer/ProfileSourcePicker";

// ── Types ─────────────────────────────────────────────────────────────────────

type FlowState =
  | { stage: "pick" }
  | { stage: "polling"; analysisId: string }
  | { stage: "done"; response: ProfileAnalysisStatusResponse };

// ── Helpers ───────────────────────────────────────────────────────────────────

function statusLabel(status: ProfileAnalysisStatus): string {
  switch (status) {
    case "Requested": return "En cola";
    case "ScrapingProfile": return "Escaneando";
    case "ProfileScraped": return "Perfil obtenido";
    case "ProfileScrapedPartial": return "Parcial";
    case "Analyzing": return "Analizando";
    case "Analyzed": return "Analizado";
    case "Failed": return "Error";
    default: return status;
  }
}

function statusBadgeVariant(
  status: ProfileAnalysisStatus
): "success" | "destructive" | "warning" | "info" | "muted" {
  switch (status) {
    case "Analyzed": return "success";
    case "Failed": return "destructive";
    case "Analyzing": return "warning";
    case "ProfileScraped":
    case "ProfileScrapedPartial": return "info";
    default: return "muted";
  }
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString("es-AR", { day: "2-digit", month: "short", year: "numeric" });
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ProfileAnalyzerModule() {
  const token = useAccessToken() || undefined;
  const [flow, setFlow] = useState<FlowState>({ stage: "pick" });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // History
  const [historyItems, setHistoryItems] = useState<ProfileAnalysisHistoryItemDto[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [historyLoaded, setHistoryLoaded] = useState(false);

  // ── Submit handler ──────────────────────────────────────────────────────────

  const handleSourcePickerSubmit = useCallback(
    async (payload: ProfileSourcePickerSubmitPayload) => {
      setIsSubmitting(true);
      setSubmitError(null);
      try {
        const resp = await analyzeProfile(
          {
            clientId: payload.clientId,
            ownAccountId: payload.ownAccountId,
            profileUrl: payload.profileUrl,
          },
          token
        );
        setFlow({ stage: "polling", analysisId: resp.id });
      } catch (err: unknown) {
        const message =
          err && typeof err === "object" && "message" in err
            ? String((err as { message: unknown }).message)
            : "Error al iniciar el análisis";
        setSubmitError(message);
      } finally {
        setIsSubmitting(false);
      }
    },
    [token]
  );

  // ── Poll complete ───────────────────────────────────────────────────────────

  const handlePollComplete = useCallback((resp: ProfileAnalysisStatusResponse) => {
    setFlow({ stage: "done", response: resp });
    // Refresh history list silently
    void loadHistory({ silent: true });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Reset to picker ─────────────────────────────────────────────────────────

  const handleReset = useCallback(() => {
    setFlow({ stage: "pick" });
    setSubmitError(null);
  }, []);

  // ── History ─────────────────────────────────────────────────────────────────

  const loadHistory = useCallback(
    async (opts?: { silent?: boolean }) => {
      if (!token) return;
      if (!opts?.silent) setHistoryLoading(true);
      try {
        const result = await listProfileAnalyses(1, token);
        setHistoryItems(result.items);
        setHistoryLoaded(true);
      } catch {
        // Keep previous list on transient error
      } finally {
        setHistoryLoading(false);
      }
    },
    [token]
  );

  // Load history on mount (once)
  const handleHistoryToggle = useCallback(() => {
    if (!historyLoaded) void loadHistory();
  }, [historyLoaded, loadHistory]);

  const handleDelete = useCallback(
    async (id: string) => {
      if (!token) return;
      setDeletingId(id);
      try {
        await deleteProfileAnalysis(id, token);
        setHistoryItems((prev) => prev.filter((i) => i.id !== id));
      } catch {
        // Ignore; item stays in list
      } finally {
        setDeletingId(null);
      }
    },
    [token]
  );

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className="space-y-6">
      {/* Source picker or progress or done state */}
      {flow.stage === "pick" && (
        <Card className="border-border/40 bg-background/60">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm">Configurar análisis de perfil</CardTitle>
          </CardHeader>
          <CardContent className="pt-0">
            <ProfileSourcePicker
              token={token}
              isSubmitting={isSubmitting}
              onSubmit={(payload) => void handleSourcePickerSubmit(payload)}
            />
            {submitError && (
              <p className="mt-2 text-xs text-destructive">{submitError}</p>
            )}
          </CardContent>
        </Card>
      )}

      {flow.stage === "polling" && (
        <div className="space-y-3">
          <AnalysisProgress
            analysisId={flow.analysisId}
            token={token}
            onComplete={handlePollComplete}
          />
          <Button variant="ghost" size="sm" onClick={handleReset} className="text-xs">
            Cancelar y volver
          </Button>
        </div>
      )}

      {flow.stage === "done" && (
        <Card className="border-green-500/20 bg-green-500/5">
          <CardContent className="pt-4 pb-4 space-y-3">
            {flow.response.status === "Analyzed" ? (
              <>
                <p className="text-sm font-medium text-green-600 dark:text-green-400">
                  Análisis completado
                </p>
                <p className="text-xs text-muted-foreground">
                  Perfil:{" "}
                  <span className="font-mono">{flow.response.profileUrlOrHandle}</span>
                </p>
                {/* WU-11 placeholder — ProfileDashboard will be rendered here */}
                <div className="rounded-md border border-border/40 border-dashed p-6 text-center text-xs text-muted-foreground/60">
                  Dashboard disponible en la siguiente versión (WU-11).
                  <br />
                  ID del análisis:{" "}
                  <span className="font-mono text-foreground/70">{flow.response.id}</span>
                </div>
              </>
            ) : (
              <>
                <p className="text-sm font-medium text-destructive">El análisis falló</p>
                {flow.response.failureCode && (
                  <p className="text-xs text-muted-foreground">
                    Código: <span className="font-mono">{flow.response.failureCode}</span>
                  </p>
                )}
              </>
            )}
            <Button variant="outline" size="sm" onClick={handleReset} className="text-xs">
              Nuevo análisis
            </Button>
          </CardContent>
        </Card>
      )}

      {/* History section */}
      <div className="space-y-2">
        <button
          type="button"
          onClick={handleHistoryToggle}
          className="flex items-center gap-2 text-xs text-muted-foreground/70 hover:text-foreground transition-colors"
        >
          <History className="h-3.5 w-3.5" />
          {historyLoaded ? "Historial de análisis" : "Ver historial"}
        </button>

        {historyLoaded && (
          <>
            {historyLoading && (
              <p className="text-xs text-muted-foreground/60">Cargando historial…</p>
            )}
            {!historyLoading && historyItems.length === 0 && (
              <p className="text-xs text-muted-foreground/60">No hay análisis previos.</p>
            )}
            {historyItems.length > 0 && (
              <Card className="border-border/40 bg-background/60">
                <CardContent className="py-2 divide-y divide-border/30">
                  {historyItems.map((item) => (
                    <div
                      key={item.id}
                      className="flex items-center gap-3 py-2.5"
                    >
                      <div className="flex-1 min-w-0">
                        <p className="text-xs font-medium text-foreground/90 truncate">
                          {item.profileUrlOrHandle}
                        </p>
                        <p className="text-xs text-muted-foreground/60">
                          {formatDate(item.createdAtUtc)}
                        </p>
                      </div>
                      <Badge
                        variant={statusBadgeVariant(
                          item.status as ProfileAnalysisStatus
                        )}
                        className="text-xs shrink-0"
                      >
                        {statusLabel(item.status as ProfileAnalysisStatus)}
                      </Badge>
                      <Button
                        size="sm"
                        variant="ghost"
                        className="h-7 px-2 text-muted-foreground hover:text-destructive"
                        disabled={deletingId === item.id}
                        onClick={() => void handleDelete(item.id)}
                        title="Eliminar análisis"
                      >
                        {deletingId === item.id ? (
                          <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                        ) : (
                          <Trash2 className="h-3.5 w-3.5" />
                        )}
                      </Button>
                    </div>
                  ))}
                </CardContent>
              </Card>
            )}
          </>
        )}
      </div>
    </div>
  );
}
