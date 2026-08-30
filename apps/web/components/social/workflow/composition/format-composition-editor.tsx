"use client";

import { Minus, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ALL_FORMAT_KEYS, FORMAT_LABELS, type ContentFormatKey } from "@/lib/api/content-generation";
import type { ContentWorkflowResult } from "@/lib/api/types";

export interface FormatCompositionEditorProps {
  result: ContentWorkflowResult | null;
  getCount: (candidateId: string, formatKey: ContentFormatKey) => number;
  onChangeCount: (candidateId: string, formatKey: ContentFormatKey, count: number) => void;
}

/**
 * Per-candidate format×count picker, driven by the real format catalog (spec: "Per-candidate format
 * composition" — reel-script, story-single, story-carousel, feed-single, feed-carousel, content-idea,
 * cta-variation, caption, hook). Replaces the old fake per-card script/carousel/story generators.
 */
export function FormatCompositionEditor({ result, getCount, onChangeCount }: FormatCompositionEditorProps) {
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
        {ALL_FORMAT_KEYS.map((formatKey) => {
          const count = getCount(result.id, formatKey);
          return (
            <div
              key={formatKey}
              className="flex items-center justify-between rounded-lg border border-border/70 bg-background/40 px-3 py-2"
            >
              <span className="text-sm">{FORMAT_LABELS[formatKey]}</span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  className="h-7 w-7 p-0"
                  disabled={count <= 0}
                  aria-label={`Decrease ${FORMAT_LABELS[formatKey]}`}
                  onClick={() => onChangeCount(result.id, formatKey, Math.max(0, count - 1))}
                >
                  <Minus className="h-3.5 w-3.5" />
                </Button>
                <span className="w-6 text-center text-sm tabular-nums">{count}</span>
                <Button
                  size="sm"
                  variant="outline"
                  className="h-7 w-7 p-0"
                  aria-label={`Increase ${FORMAT_LABELS[formatKey]}`}
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
