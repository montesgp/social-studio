"use client";

// Shared full-screen blocking overlay wrapping AnalysisProgressCard.
// Used by both the content analyzer (single-analyzer-client) and the
// profile analyzer (ProfileAnalyzerModule) so they are visually and
// behaviorally identical — same size, same backdrop, same card.
// Only the steps, titles, and status-mapping logic differ per tool.

import { AnalysisProgressCard, type AnalysisStep } from "@/components/social/analysis-progress-card";

export interface AnalysisOverlayProps {
  /** When null/undefined the overlay is hidden. */
  visible: boolean;
  /** Ordered pipeline steps. */
  steps: AnalysisStep[];
  /** 0-based index of the active step (use steps.length when terminal). */
  currentStep: number;
  /** Overall completion percentage (0-100). */
  percent: number;
  /** True when the analysis finished successfully. */
  isDone: boolean;
  /** True when the analysis is in a failed/timed-out state. */
  isFailed: boolean;
  /** True when the scrape completed partially. */
  isPartial?: boolean;
  /** Human-readable label shown in the header while running. */
  runningTitle?: string;
  /** Human-readable label shown when done. */
  doneTitle?: string;
  /** Human-readable label shown when failed. */
  failedTitle?: string;
  /** Short description shown below the title. */
  description?: string;
  /** Optional error detail rendered below the step list when failed. */
  failureDetail?: string | null;
  /** aria-label for the dialog backdrop (defaults to runningTitle). */
  ariaLabel?: string;
}

export function AnalysisOverlay({
  visible,
  steps,
  currentStep,
  percent,
  isDone,
  isFailed,
  isPartial,
  runningTitle,
  doneTitle,
  failedTitle,
  description,
  failureDetail,
  ariaLabel,
}: AnalysisOverlayProps) {
  if (!visible) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={ariaLabel ?? runningTitle ?? "Analysis in progress"}
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 backdrop-blur-sm"
    >
      <AnalysisProgressCard
        steps={steps}
        currentStep={currentStep}
        percent={percent}
        isDone={isDone}
        isFailed={isFailed}
        isPartial={isPartial}
        runningTitle={runningTitle}
        doneTitle={doneTitle}
        failedTitle={failedTitle}
        description={description}
        failureDetail={failureDetail}
      />
    </div>
  );
}
