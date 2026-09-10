"use client";

// Shared analyzer tools toolbar used by both the content analyzer
// (single-analyzer-client) and the profile analyzer (ProfileAnalyzerModule).
//
// Visibility logic (mirrors single-analyzer-client):
//   - On "new analysis" screen (showResultActions=false) → Help only.
//   - When viewing a result (fresh or from history) → New, Export, Help.
//
// The wrench toggle button hides/shows the action group with a tooltip.
//
// Help behaviour:
//   - Without a copyable job id, Help is a single button that opens support
//     directly (backward-compatible: this is what ProfileAnalyzerModule uses).
//   - With a job id (jobId + onCopyJobId provided), Help becomes a small menu
//     exposing "Contact support" plus "Copy Job ID" so the generated job can be
//     traced later (logs, format results). Click → copy → toast, wallet style.

import { useEffect, useRef, useState } from "react";
import { Wrench, LifeBuoy, Download, Plus, ChevronDown, Copy } from "lucide-react";
import { Button } from "@/components/ui/button";

export interface AnalyzerToolsToolbarProps {
  /** When true, the Export button is visible — there's a completed analysis to export. */
  showResultActions: boolean;
  /**
   * When true, the New button is visible. Defaults to `showResultActions` for
   * backward-compatible callers that never distinguished the two. A caller that also wants
   * New available on a FAILED analysis (nothing to export, but the user still needs an easy
   * way to start over next to Help) should pass this independently — see single-analyzer-client.
   */
  showNewAction?: boolean;
  /** Called when Export is clicked. Kept as onSave for backward-compatible callers. */
  onSave: () => void;
  /** Called when New / New analysis is clicked. */
  onNew: () => void;
  /** Called when Help / Contact support is clicked. */
  onHelp: () => void;
  /** Disable Export and New while operations are in flight. */
  actionsDisabled?: boolean;
  /** Label for the New action button (default: "New analysis"). */
  newLabel?: string;
  /** Label for the Export action button (default: "Export"). */
  saveLabel?: string;
  /**
   * Current generated job id, when available. When set together with
   * onCopyJobId, Help turns into a menu exposing a "Copy Job ID" action.
   */
  jobId?: string | null;
  /** Invoked when the user picks "Copy Job ID". Required for the item to show. */
  onCopyJobId?: () => void;
}

export function AnalyzerToolsToolbar({
  showResultActions,
  showNewAction,
  onSave,
  onNew,
  onHelp,
  actionsDisabled = false,
  newLabel = "New analysis",
  saveLabel = "Export",
  jobId = null,
  onCopyJobId,
}: AnalyzerToolsToolbarProps) {
  const showNew = showNewAction ?? showResultActions;
  const [isOpen, setIsOpen] = useState(true);
  const [helpMenuOpen, setHelpMenuOpen] = useState(false);
  const helpRef = useRef<HTMLDivElement>(null);

  // Help only needs the menu treatment when there's a job id to copy.
  const hasCopyableJob = Boolean(jobId && onCopyJobId);

  useEffect(() => {
    if (!helpMenuOpen) return;

    function handlePointerDown(event: MouseEvent) {
      if (helpRef.current && !helpRef.current.contains(event.target as Node)) {
        setHelpMenuOpen(false);
      }
    }
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setHelpMenuOpen(false);
    }

    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [helpMenuOpen]);

  return (
    <div className="flex items-center gap-1.5" data-pdf-exclude="true">
      <Button
        size="sm"
        variant="outline"
        className="h-8 w-8 p-0"
        onClick={() => setIsOpen((open) => !open)}
        aria-label={isOpen ? "Hide analyzer tools" : "Show analyzer tools"}
        title={isOpen ? "Hide analyzer tools" : "Show analyzer tools"}
      >
        <Wrench className="h-3.5 w-3.5" />
      </Button>
      {isOpen && (
        <div className="flex items-center gap-1.5 rounded-md border border-border/40 bg-background/60 p-1">
          {hasCopyableJob ? (
            <div ref={helpRef} className="relative">
              <Button
                size="sm"
                variant="ghost"
                className="h-7 px-2 text-xs"
                onClick={() => setHelpMenuOpen((open) => !open)}
                aria-haspopup="menu"
                aria-expanded={helpMenuOpen}
              >
                <LifeBuoy className="h-3.5 w-3.5" />
                Help
                <ChevronDown className="h-3 w-3 opacity-60" />
              </Button>
              {helpMenuOpen && (
                <div
                  role="menu"
                  aria-label="Help options"
                  className="absolute right-0 z-50 mt-1 min-w-[12rem] overflow-hidden rounded-md border border-border bg-card py-1 shadow-lg"
                >
                  <button
                    type="button"
                    role="menuitem"
                    className="flex w-full items-center gap-2 px-3 py-2 text-left text-xs hover:bg-muted"
                    onClick={() => {
                      setHelpMenuOpen(false);
                      onHelp();
                    }}
                  >
                    <LifeBuoy className="h-3.5 w-3.5" />
                    Contact support
                  </button>
                  <button
                    type="button"
                    role="menuitem"
                    className="flex w-full items-center gap-2 px-3 py-2 text-left text-xs hover:bg-muted"
                    onClick={() => {
                      setHelpMenuOpen(false);
                      onCopyJobId?.();
                    }}
                  >
                    <Copy className="h-3.5 w-3.5" />
                    Copy Job ID
                  </button>
                </div>
              )}
            </div>
          ) : (
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={onHelp}
            >
              <LifeBuoy className="h-3.5 w-3.5" />
              Help
            </Button>
          )}
          {showResultActions && (
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={onSave}
              disabled={actionsDisabled}
            >
              <Download className="h-3.5 w-3.5" />
              {saveLabel}
            </Button>
          )}
          {showNew && (
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={onNew}
              disabled={actionsDisabled}
            >
              <Plus className="h-3.5 w-3.5" />
              {newLabel}
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
