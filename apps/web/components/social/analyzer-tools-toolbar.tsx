"use client";

// Shared analyzer tools toolbar used by both the content analyzer
// (single-analyzer-client) and the profile analyzer (ProfileAnalyzerModule).
//
// Visibility logic (mirrors single-analyzer-client):
//   - On "new analysis" screen (showResultActions=false) → Help only.
//   - When viewing a result (fresh or from history) → New, Export, Help.
//
// The wrench toggle button hides/shows the action group with a tooltip.

import { useState } from "react";
import { Wrench, LifeBuoy, Download, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";

export interface AnalyzerToolsToolbarProps {
  /** When true, Export and New buttons are visible (result is being viewed). */
  showResultActions: boolean;
  /** Called when Export is clicked. Kept as onSave for backward-compatible callers. */
  onSave: () => void;
  /** Called when New / New analysis is clicked. */
  onNew: () => void;
  /** Called when Help is clicked. */
  onHelp: () => void;
  /** Disable Export and New while operations are in flight. */
  actionsDisabled?: boolean;
  /** Label for the New action button (default: "New analysis"). */
  newLabel?: string;
  /** Label for the Export action button (default: "Export"). */
  saveLabel?: string;
}

export function AnalyzerToolsToolbar({
  showResultActions,
  onSave,
  onNew,
  onHelp,
  actionsDisabled = false,
  newLabel = "New analysis",
  saveLabel = "Export",
}: AnalyzerToolsToolbarProps) {
  const [isOpen, setIsOpen] = useState(true);

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
          <Button
            size="sm"
            variant="ghost"
            className="h-7 px-2 text-xs"
            onClick={onHelp}
          >
            <LifeBuoy className="h-3.5 w-3.5" />
            Help
          </Button>
          {showResultActions && (
            <>
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
            </>
          )}
        </div>
      )}
    </div>
  );
}
