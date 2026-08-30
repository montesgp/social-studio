"use client";

// StageOverlay — the ONE full-screen blocking overlay shell used everywhere in the app.
// Replaces the previous BlockingLoader (indeterminate/percent progress bar) and the
// AnalysisOverlay + AnalysisProgressCard pair (percent bar + step list). This component shows
// the real stage a background operation is in — never a fabricated percentage or animation.
//
// Any tool declares its own ordered list of real stages and moves the active one forward as work
// progresses (directly, or via the shared pub/sub store in @/lib/loading/blocking-loader-store).

import { AnimatePresence, motion } from "framer-motion";
import { CheckCircle2, Loader2, RefreshCw, XCircle } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  activeStageIndex,
  resolveStageStatuses,
  type OverlayOutcome,
  type OverlayStage,
} from "@/lib/stage-overlay-state";

export type { OverlayStage as StageOverlayStage, OverlayOutcome as StageOverlayOutcome };

export interface StageOverlayProps {
  /** When false the overlay fades out and unmounts. */
  visible: boolean;
  /** Ordered, real stages — a tool with nothing granular to report may pass a single stage. */
  stages: readonly OverlayStage[];
  /** Key of the currently active stage. Defaults to the first stage when omitted. */
  activeKey?: string | null;
  /** Terminal state of the operation. Defaults to "running". */
  outcome?: OverlayOutcome;
  /** True when the operation completed partially (amber badge next to the title). */
  isPartial?: boolean;
  /** Header title while running. */
  runningTitle: string;
  /** Header title on success. Defaults to runningTitle. */
  doneTitle?: string;
  /** Header title on failure. Defaults to runningTitle. */
  failedTitle?: string;
  /** Optional short description shown under the title. */
  description?: string;
  /** Error detail rendered below the stage list when outcome is "failed". */
  failureDetail?: string | null;
  /** aria-label for the dialog (defaults to the resolved title). */
  ariaLabel?: string;
  className?: string;
}

export function StageOverlay({
  visible,
  stages,
  activeKey = null,
  outcome = "running",
  isPartial = false,
  runningTitle,
  doneTitle,
  failedTitle,
  description,
  failureDetail,
  ariaLabel,
  className,
}: StageOverlayProps) {
  const activeIndex = activeStageIndex(stages, activeKey);
  const statuses = resolveStageStatuses(stages, activeIndex, outcome);
  const title = outcome === "failed" ? failedTitle ?? runningTitle : outcome === "done" ? doneTitle ?? runningTitle : runningTitle;

  return (
    <AnimatePresence>
      {visible ? (
        <motion.div
          key="stage-overlay"
          role="dialog"
          aria-modal="true"
          aria-label={ariaLabel ?? title}
          className={cn(
            "fixed inset-0 z-[70] flex items-center justify-center bg-background/85 backdrop-blur-sm",
            className
          )}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.18, ease: "easeOut" }}
        >
          <div className="w-full max-w-md rounded-2xl border border-border/50 bg-card p-6 shadow-2xl space-y-5">
            {/* Header */}
            <div className="flex items-center gap-3">
              <div
                className={cn(
                  "rounded-lg p-2",
                  outcome === "failed" ? "bg-destructive/10" : outcome === "done" ? "bg-green-500/10" : "bg-primary/10"
                )}
              >
                {outcome === "failed" ? (
                  <XCircle className="h-5 w-5 text-destructive" />
                ) : outcome === "done" ? (
                  <CheckCircle2 className="h-5 w-5 text-green-500" />
                ) : (
                  <Loader2 className="h-5 w-5 animate-spin text-primary" />
                )}
              </div>
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="text-sm font-semibold text-foreground">{title}</h3>
                  {isPartial && outcome === "running" ? (
                    <span className="text-xs px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20">
                      partial
                    </span>
                  ) : null}
                </div>
                {description ? (
                  <p className="text-xs text-muted-foreground">{description}</p>
                ) : null}
              </div>
            </div>

            {/* Stage list — the real progress signal, no invented percentage */}
            <ol className="space-y-2">
              {stages.map((stage, i) => {
                const state = statuses[i];
                return (
                  <li key={stage.key} className="flex items-center gap-2.5 text-xs">
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
                    <span className={cn(state === "pending" ? "text-muted-foreground/50" : "text-foreground/90")}>
                      {stage.label}
                    </span>
                  </li>
                );
              })}
            </ol>

            {/* Failure detail */}
            {outcome === "failed" && failureDetail ? (
              <p className="text-xs text-destructive">{failureDetail}</p>
            ) : null}
          </div>
        </motion.div>
      ) : null}
    </AnimatePresence>
  );
}
