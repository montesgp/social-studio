"use client";

// Shared analysis-progress UI used by both the content analyzer (single-analyzer-client)
// and the profile analyzer (ProfileAnalyzerModule).
// Purely presentational — polling and status-mapping live in each caller.

import { ScanSearch, CheckCircle2, RefreshCw, XCircle } from "lucide-react";
import { cn } from "@/lib/utils";

export interface AnalysisStep {
  key: string;
  label: string;
}

export interface AnalysisProgressCardProps {
  /** Ordered list of pipeline steps to render. */
  steps: AnalysisStep[];
  /** 0-based index of the active step (use steps.length when terminal). */
  currentStep: number;
  /** Overall completion percentage (0-100). */
  percent: number;
  /** True when the analysis finished successfully. */
  isDone: boolean;
  /** True when the analysis is in a failed/timed-out state. */
  isFailed: boolean;
  /** True when the scrape completed partially (amber badge). */
  isPartial?: boolean;
  /** Human-readable label shown in the header while running. */
  runningTitle?: string;
  /** Human-readable label shown when done. */
  doneTitle?: string;
  /** Human-readable label shown when failed. */
  failedTitle?: string;
  /** Short description shown in the header (optional). */
  description?: string;
  /** Optional error detail rendered below the step list when failed. */
  failureDetail?: string | null;
}

export function AnalysisProgressCard({
  steps,
  currentStep,
  percent,
  isDone,
  isFailed,
  isPartial = false,
  runningTitle = "Analizando…",
  doneTitle = "Análisis completado",
  failedTitle = "El análisis falló",
  description,
  failureDetail,
}: AnalysisProgressCardProps) {
  const title = isFailed ? failedTitle : isDone ? doneTitle : runningTitle;

  return (
    <div className="w-full max-w-md rounded-2xl border border-border/50 bg-card p-6 shadow-2xl space-y-5">
      {/* Header */}
      <div className="flex items-center gap-3">
        <div
          className={cn(
            "rounded-lg p-2",
            isFailed ? "bg-destructive/10" : isDone ? "bg-green-500/10" : "bg-purple-500/10"
          )}
        >
          {isFailed ? (
            <XCircle className="h-5 w-5 text-destructive" />
          ) : isDone ? (
            <CheckCircle2 className="h-5 w-5 text-green-500" />
          ) : (
            <ScanSearch className="h-5 w-5 text-purple-400 animate-pulse" />
          )}
        </div>
        <div>
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-foreground">{title}</h3>
            {isPartial && !isDone && !isFailed && (
              <span className="text-xs px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20">
                parcial
              </span>
            )}
          </div>
          {description ? (
            <p className="text-xs text-muted-foreground">{description}</p>
          ) : null}
        </div>
      </div>

      {/* Progress bar */}
      <div className="space-y-2">
        <div className="flex items-center justify-between text-xs">
          <span className="text-muted-foreground">
            {steps[Math.min(currentStep, steps.length - 1)]?.label ?? ""}
          </span>
          <span className="font-medium text-foreground tabular-nums">{percent}%</span>
        </div>
        <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
          <div
            className={cn(
              "h-full rounded-full transition-all duration-500 ease-out",
              isFailed
                ? "bg-destructive"
                : isDone
                ? "bg-green-500"
                : "bg-gradient-to-r from-purple-500 to-blue-500"
            )}
            style={{ width: `${percent}%` }}
          />
        </div>
      </div>

      {/* Step list */}
      <ol className="space-y-2">
        {steps.map((step, i) => {
          const state =
            i < currentStep ? "done" : i === currentStep && !isDone && !isFailed ? "active" : "pending";
          return (
            <li key={step.key} className="flex items-center gap-2.5 text-xs">
              <span
                className={cn(
                  "flex h-5 w-5 items-center justify-center rounded-full border shrink-0",
                  state === "done" && "border-green-500/50 bg-green-500/15 text-green-500",
                  state === "active" && "border-purple-500/50 bg-purple-500/15 text-purple-400",
                  state === "pending" && "border-border/50 bg-muted/40 text-muted-foreground/50"
                )}
              >
                {state === "done" ? (
                  <CheckCircle2 className="h-3 w-3" />
                ) : state === "active" ? (
                  <RefreshCw className="h-3 w-3 animate-spin" />
                ) : (
                  <span className="h-1.5 w-1.5 rounded-full bg-current opacity-50" />
                )}
              </span>
              <span
                className={cn(
                  state === "pending" ? "text-muted-foreground/50" : "text-foreground/90"
                )}
              >
                {step.label}
              </span>
            </li>
          );
        })}
      </ol>

      {/* Failure detail */}
      {isFailed && failureDetail ? (
        <p className="text-xs text-destructive">{failureDetail}</p>
      ) : null}
    </div>
  );
}
