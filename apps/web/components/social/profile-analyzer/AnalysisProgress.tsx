"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { RefreshCw, CheckCircle2, XCircle } from "lucide-react";
import { cn } from "@/lib/utils";
import { Card, CardContent } from "@/components/ui/card";
import { getProfileAnalysis } from "@/lib/api/profile-analyzer";
import type {
  ProfileAnalysisStatusResponse,
  ProfileAnalysisStatus,
} from "@/lib/api/types/profile-analyzer";

// ── Constants ─────────────────────────────────────────────────────────────────

const POLL_INTERVAL_MS = 2500;
const POLL_TIMEOUT_MS = 5 * 60 * 1000; // 5 min hard cap

const TERMINAL_STATUSES: ProfileAnalysisStatus[] = ["Analyzed", "Failed"];

// Ordered pipeline steps — maps backend status values to human-readable labels.
const PIPELINE_STEPS: Array<{
  key: string;
  label: string;
  statuses: ProfileAnalysisStatus[];
}> = [
  { key: "queued", label: "En cola", statuses: ["Requested"] },
  { key: "scraping", label: "Escaneando perfil", statuses: ["ScrapingProfile"] },
  {
    key: "scraped",
    label: "Perfil obtenido",
    statuses: ["ProfileScraped", "ProfileScrapedPartial"],
  },
  { key: "analyzing", label: "Analizando con IA", statuses: ["Analyzing"] },
];

function statusToPercent(status: ProfileAnalysisStatus | null): number {
  switch (status) {
    case "Requested":
      return 8;
    case "ScrapingProfile":
      return 30;
    case "ProfileScraped":
    case "ProfileScrapedPartial":
      return 60;
    case "Analyzing":
      return 82;
    case "Analyzed":
      return 100;
    case "Failed":
      return 100;
    default:
      return 0;
  }
}

function statusToStepIndex(status: ProfileAnalysisStatus | null): number {
  if (!status) return 0;
  if (status === "Analyzed" || status === "Failed") return PIPELINE_STEPS.length;
  const idx = PIPELINE_STEPS.findIndex((s) =>
    s.statuses.includes(status as ProfileAnalysisStatus)
  );
  return idx === -1 ? 0 : idx;
}

// ── Props ─────────────────────────────────────────────────────────────────────

interface AnalysisProgressProps {
  analysisId: string;
  token?: string;
  /** Called when the analysis reaches a terminal status (Analyzed or Failed). */
  onComplete: (response: ProfileAnalysisStatusResponse) => void;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function AnalysisProgress({
  analysisId,
  token,
  onComplete,
}: AnalysisProgressProps) {
  const [status, setStatus] = useState<ProfileAnalysisStatus | null>(null);
  const [isPartial, setIsPartial] = useState(false);
  const [failureCode, setFailureCode] = useState<string | null>(null);
  const [timedOut, setTimedOut] = useState(false);
  const pollCancelRef = useRef(false);
  const startTimeRef = useRef(Date.now());

  const poll = useCallback(async () => {
    pollCancelRef.current = false;
    startTimeRef.current = Date.now();

    const tick = async () => {
      if (pollCancelRef.current) return;

      if (Date.now() - startTimeRef.current > POLL_TIMEOUT_MS) {
        setTimedOut(true);
        return;
      }

      try {
        const resp = await getProfileAnalysis(analysisId, token);
        setStatus(resp.status as ProfileAnalysisStatus);
        setIsPartial(resp.status === "ProfileScrapedPartial");
        if (resp.failureCode) setFailureCode(resp.failureCode);

        if (TERMINAL_STATUSES.includes(resp.status as ProfileAnalysisStatus)) {
          onComplete(resp);
          return;
        }
      } catch {
        // Transient error — keep polling; the timeout guard will stop infinite loops.
      }

      await new Promise<void>((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      void tick();
    };

    void tick();
  }, [analysisId, token, onComplete]);

  useEffect(() => {
    void poll();
    return () => {
      pollCancelRef.current = true;
    };
  }, [poll]);

  const percent = statusToPercent(status);
  const stepIndex = statusToStepIndex(status);
  const isFailed = status === "Failed" || timedOut;
  const isDone = status === "Analyzed";

  return (
    <Card className="border-border/40 bg-background/60">
      <CardContent className="pt-4 pb-4 space-y-4">
        {/* Header */}
        <div className="flex items-center gap-2">
          {isFailed ? (
            <XCircle className="h-4 w-4 text-destructive shrink-0" />
          ) : isDone ? (
            <CheckCircle2 className="h-4 w-4 text-green-500 shrink-0" />
          ) : (
            <RefreshCw className="h-4 w-4 text-purple-400 animate-spin shrink-0" />
          )}
          <span className="text-sm font-medium text-foreground">
            {isFailed
              ? "El análisis falló"
              : isDone
              ? "Análisis completado"
              : "Analizando perfil…"}
          </span>
          {isPartial && !isDone && !isFailed && (
            <span className="text-xs px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20">
              parcial
            </span>
          )}
        </div>

        {/* Progress bar */}
        <div className="h-1.5 w-full rounded-full bg-muted overflow-hidden">
          <div
            className={cn(
              "h-full rounded-full transition-all duration-500",
              isFailed
                ? "bg-destructive"
                : isDone
                ? "bg-green-500"
                : "bg-purple-500"
            )}
            style={{ width: `${percent}%` }}
          />
        </div>

        {/* Step labels */}
        <div className="flex gap-1.5 flex-wrap">
          {PIPELINE_STEPS.map((step, idx) => {
            const isActive = idx === stepIndex && !isDone && !isFailed;
            const isComplete = idx < stepIndex || isDone;
            return (
              <span
                key={step.key}
                className={cn(
                  "text-xs px-2 py-0.5 rounded-full border transition-colors",
                  isActive &&
                    "border-purple-500/40 bg-purple-500/10 text-purple-400",
                  isComplete &&
                    "border-green-500/30 bg-green-500/10 text-green-600 dark:text-green-400",
                  !isActive &&
                    !isComplete &&
                    "border-border/40 text-muted-foreground/50"
                )}
              >
                {step.label}
              </span>
            );
          })}
        </div>

        {/* Error message */}
        {isFailed && (
          <p className="text-xs text-destructive">
            {timedOut
              ? "El análisis tardó demasiado. Podés reintentar desde el historial."
              : failureCode
              ? `Código de error: ${failureCode}`
              : "El análisis encontró un error inesperado."}
          </p>
        )}
      </CardContent>
    </Card>
  );
}
