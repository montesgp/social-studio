"use client";

import { useEffect, useState } from "react";
import { FileText, Send } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { ReviewStateBadge } from "@/components/social/publications/publication-status-badge";
import type { ContentBankItemResponse } from "@/lib/api/publications";
import { fetchContentItemAsset, type ContentReviewState } from "@/lib/api/content-studio";
import { useTheme } from "@/components/theme/theme-provider";
import { formatUserDate } from "@/lib/format/date";
import { resolveBankThumbnail } from "@/lib/publications/thumbnail";

const PLANNER_ACTIONABLE = new Set<ContentReviewState>(["Approved", "ReadyForPublication"]);

const STATE_FILTERS: ContentReviewState[] = [
  "Draft",
  "InReview",
  "Edited",
  "Approved",
  "Rejected",
  "ReadyForPublication",
];

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
  token: string;
}

/** Best-effort snippet of an item's copy for the graceful (no-image) preview card. */
function previewSnippet(item: ContentBankItemResponse): string {
  const source = item.caption?.trim() || item.text?.trim() || "";
  if (!source) return "";
  return source.length > 90 ? `${source.slice(0, 90)}…` : source;
}

/**
 * Renders an item's thumbnail: the resolved markup/background image when one exists, otherwise the
 * first raw asset (fetched the same way Content Studio's `AssetFallbackImage` does), otherwise a
 * graceful text/format preview instead of a broken-image icon.
 */
function ItemThumbnail({ item, token }: { item: ContentBankItemResponse; token: string }) {
  const markupThumbnail = resolveBankThumbnail(item.visualSpecMarkup);
  const [assetUrl, setAssetUrl] = useState<string | null>(null);
  const firstAsset = item.assets[0];

  useEffect(() => {
    setAssetUrl(null);
    if (markupThumbnail || !firstAsset) return;

    if (/^(https?:|data:|blob:|\/)/i.test(firstAsset.storageUri)) {
      setAssetUrl(firstAsset.storageUri);
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;
    fetchContentItemAsset(item.id, 0, token)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setAssetUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setAssetUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item.id, markupThumbnail, firstAsset?.storageUri, token]);

  const thumbnail = markupThumbnail ?? assetUrl;

  if (thumbnail) {
    return (
      <div
        className="h-full w-full bg-cover bg-center"
        style={{ backgroundImage: `url(${thumbnail})` }}
        aria-hidden="true"
      />
    );
  }

  const snippet = previewSnippet(item);
  return (
    <div className="flex h-full w-full flex-col items-center justify-center gap-1.5 p-3 text-center">
      <FileText className="h-5 w-5 text-muted-foreground" />
      {snippet ? (
        <p className="line-clamp-3 text-[11px] text-muted-foreground">{snippet}</p>
      ) : (
        <span className="text-[11px] text-muted-foreground">{item.formatKey}</span>
      )}
    </div>
  );
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
  token,
}: ContentBankGridProps) {
  const { dateFormat } = useTheme();
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
            const canPublish = PLANNER_ACTIONABLE.has(item.state);
            return (
              <div
                key={item.id}
                className="group flex flex-col overflow-hidden rounded-lg border border-border bg-background shadow-sm"
              >
                <button
                  type="button"
                  onClick={() => onOpenItem(item)}
                  className={cn("relative flex aspect-square items-center justify-center bg-muted/40")}
                >
                  <ItemThumbnail item={item} token={token} />
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
                  <span className="text-[11px] text-muted-foreground">
                    Updated {formatUserDate(item.updatedAtUtc, dateFormat)}
                  </span>
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
