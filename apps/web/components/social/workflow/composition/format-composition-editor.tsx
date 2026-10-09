"use client";

import { Minus, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { ContentFormatKey } from "@/lib/api/content-generation";
import type { SelectableFormatOption } from "@/lib/social/format-catalog";
import type { ContentWorkflowResult } from "@/lib/api/types";

export interface FormatCompositionEditorProps {
  result: ContentWorkflowResult | null;
  /**
   * Server-driven format catalog (see `hooks/use-format-catalog.ts`), already filtered to exclude
   * auxiliary formats (content-idea, cta-variation, caption, hook) — those are auto-bundled into their
   * parent deliverable at generation time and must never be directly selectable here.
   */
  formatOptions: SelectableFormatOption[];
  getCount: (candidateId: string, formatKey: ContentFormatKey) => number;
  onChangeCount: (candidateId: string, formatKey: ContentFormatKey, count: number) => void;
}

/**
 * Per-candidate format×count picker, driven by the real format catalog (spec: "Per-candidate format
 * composition"). Only surface/layout-selectable deliverables are offered (reel-script, story-single,
 * story-carousel, feed-single, feed-carousel) — auxiliary formats are bundled automatically and are
 * never user-selectable. Replaces the old fake per-card script/carousel/story generators.
 */
export function FormatCompositionEditor({ result, formatOptions, getCount, onChangeCount }: FormatCompositionEditorProps) {
  if (!result) {
    return <p className="text-sm text-muted-foreground">Select a candidate to compose its formats.</p>;
  }

  return (
    <div className="space-y-3">
      <div className="rounded-xl border border-border bg-background/40 p-3">
        <p className="font-medium">{result.title}</p>
        <p className="mt-1 text-xs text-muted-foreground">{result.summary}</p>
      </div>

      <div className="space-y-2">
        {formatOptions.length === 0 && (
          <p className="text-sm text-muted-foreground">Loading available formats…</p>
        )}
        {formatOptions.map(({ formatKey, label }) => {
          const count = getCount(result.id, formatKey);
          return (
            <div
              key={formatKey}
              className="flex items-center justify-between rounded-lg border border-border/70 bg-background/40 px-3 py-2"
            >
              <span className="text-sm">{label}</span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  className="h-7 w-7 p-0"
                  disabled={count <= 0}
                  aria-label={`Decrease ${label}`}
                  onClick={() => onChangeCount(result.id, formatKey, Math.max(0, count - 1))}
                >
                  <Minus className="h-3.5 w-3.5" />
                </Button>
                <span className="w-6 text-center text-sm tabular-nums">{count}</span>
                <Button
                  size="sm"
                  variant="outline"
                  className="h-7 w-7 p-0"
                  aria-label={`Increase ${label}`}
                  onClick={() => onChangeCount(result.id, formatKey, count + 1)}
                >
                  <Plus className="h-3.5 w-3.5" />
                </Button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
