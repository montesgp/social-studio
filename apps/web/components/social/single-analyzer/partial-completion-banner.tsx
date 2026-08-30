"use client";

import { AlertTriangle } from "lucide-react";
import { cn } from "@/lib/utils";
import type { ContentFormatKey } from "@/lib/api/types";
import { FORMAT_LABELS } from "@/lib/api/content-generation";

interface PartialCompletionBannerProps {
  succeededFormats: ContentFormatKey[];
  failedFormats: ContentFormatKey[];
  className?: string;
}

/**
 * Banner shown when a content generation job completes with partial success.
 * Lists succeeded formats (green chips) and failed formats (muted chips).
 */
export function PartialCompletionBanner({
  succeededFormats,
  failedFormats,
  className,
}: PartialCompletionBannerProps) {
  return (
    <div
      className={cn(
        "rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 space-y-2",
        className
      )}
    >
      <div className="flex items-start gap-2">
        <AlertTriangle className="h-4 w-4 text-amber-500 mt-0.5 shrink-0" />
        <div className="flex-1 min-w-0 space-y-1.5">
          <p className="text-xs font-medium text-foreground">Partial generation complete</p>
          <p className="text-[11px] text-muted-foreground">
            You can still download the partial package.
          </p>
        </div>
      </div>

      {succeededFormats.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {succeededFormats.map((fmt) => (
            <span
              key={fmt}
              className="inline-flex items-center rounded px-1.5 py-0.5 text-[10px] bg-green-500/10 text-green-600 dark:text-green-400 border border-green-500/20"
            >
              {FORMAT_LABELS[fmt]}
            </span>
          ))}
        </div>
      )}

      {failedFormats.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {failedFormats.map((fmt) => (
            <span
              key={fmt}
              className="inline-flex items-center rounded px-1.5 py-0.5 text-[10px] bg-muted/40 text-muted-foreground border border-border/30 line-through"
            >
              {FORMAT_LABELS[fmt]}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
