"use client";

import { ImageOff, Send } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { ReviewStateBadge } from "@/components/social/publications/publication-status-badge";
import type { ContentBankItemResponse } from "@/lib/api/publications";
import type { ContentReviewState } from "@/lib/api/content-studio";

const PLANNER_ACTIONABLE = new Set<ContentReviewState>(["Approved", "ReadyForPublication"]);

const STATE_FILTERS: ContentReviewState[] = [
  "Draft",
  "InReview",
  "Edited",
  "Approved",
  "Rejected",
  "ReadyForPublication",
];

/** Pulls the flattened background data-URI out of a resolved visual-spec envelope, if any. */
function resolveThumbnail(visualSpecMarkup: string | null): string | null {
  if (!visualSpecMarkup) return null;
  try {
    const parsed = JSON.parse(visualSpecMarkup) as unknown;
    const envelope = Array.isArray(parsed) ? parsed[0] : parsed;
    const ref = (envelope as { flattenedImageRef?: string | null } | undefined)?.flattenedImageRef;
    return typeof ref === "string" ? ref : null;
  } catch {
    return null;
  }
}

interface ContentBankGridProps {
  items: ContentBankItemResponse[];
  loading: boolean;
  fromUtc: string;
  toUtc: string;
  onRangeChange: (fromUtc: string, toUtc: string) => void;
  stateFilter: ContentReviewState | null;
  onStateFilterChange: (state: ContentReviewState | null) => void;
  onOpenItem: (item: ContentBankItemResponse) => void;
  onPublishNow: (item: ContentBankItemResponse) => void;
}

/** Bank tab (design §10): catalog/grid of item-cards across all sets/origins for the client. */
export function ContentBankGrid({
  items,
  loading,
  fromUtc,
  toUtc,
  onRangeChange,
  stateFilter,
  onStateFilterChange,
  onOpenItem,
  onPublishNow,
}: ContentBankGridProps) {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-xs text-muted-foreground">
          From
          <Input
            type="date"
            value={fromUtc.slice(0, 10)}
            onChange={(e) => onRangeChange(new Date(e.target.value).toISOString(), toUtc)}
            className="w-40"
          />
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted-foreground">
          To
          <Input
            type="date"
            value={toUtc.slice(0, 10)}
            onChange={(e) => onRangeChange(fromUtc, new Date(e.target.value).toISOString())}
            className="w-40"
          />
        </label>
      </div>

      <div className="flex flex-wrap gap-1.5">
        <Badge
          variant={stateFilter === null ? "default" : "outline"}
          className="cursor-pointer"
          onClick={() => onStateFilterChange(null)}
        >
          All
        </Badge>
        {STATE_FILTERS.map((state) => (
          <Badge
            key={state}
            variant={stateFilter === state ? "default" : "outline"}
            className="cursor-pointer"
            onClick={() => onStateFilterChange(state)}
          >
            {state}
          </Badge>
        ))}
      </div>

      {loading ? (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} className="aspect-square animate-pulse rounded-lg border border-border bg-muted/40" />
          ))}
        </div>
      ) : items.length === 0 ? (
        <div className="rounded-lg border border-border bg-muted/20 p-8 text-center text-sm text-muted-foreground">
          No content in this range. Widen the date range or generate content in Content Studio.
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {items.map((item) => {
            const thumbnail = resolveThumbnail(item.visualSpecMarkup);
            const canPublish = PLANNER_ACTIONABLE.has(item.state);
            return (
              <div
                key={item.id}
                className="group flex flex-col overflow-hidden rounded-lg border border-border bg-background shadow-sm"
              >
                <button
                  type="button"
                  onClick={() => onOpenItem(item)}
                  className={cn(
                    "relative flex aspect-square items-center justify-center bg-muted/40 bg-cover bg-center",
                  )}
                  style={thumbnail ? { backgroundImage: `url(${thumbnail})` } : undefined}
                >
                  {!thumbnail ? <ImageOff className="h-6 w-6 text-muted-foreground" /> : null}
                </button>
                <div className="flex flex-col gap-2 p-2">
                  <div className="flex items-center justify-between gap-1">
                    <span className="truncate text-xs font-medium">{item.formatKey}</span>
                    <ReviewStateBadge state={item.state} />
                  </div>
                  {item.publications.length > 0 ? (
                    <span className="text-[11px] text-muted-foreground">
                      {item.publications.length} publication{item.publications.length === 1 ? "" : "s"}
                    </span>
                  ) : null}
                  {canPublish ? (
                    <Button size="sm" variant="default" onClick={() => onPublishNow(item)}>
                      <Send className="h-3.5 w-3.5" /> Publish now
                    </Button>
                  ) : null}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
