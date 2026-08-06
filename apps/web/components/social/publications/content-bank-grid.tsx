"use client";

import { useEffect, useState } from "react";
import { Check, Download, FileText, Send, SendHorizonal, X } from "lucide-react";
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
import { canPublishItem, reviewActionsFor } from "@/lib/publications/review-actions";
import { textPreviewLines } from "@/lib/publications/reel-script";

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
  busy?: boolean;
  fromUtc: string;
  toUtc: string;
  onRangeChange: (fromUtc: string, toUtc: string) => void;
  stateFilter: ContentReviewState | null;
  onStateFilterChange: (state: ContentReviewState | null) => void;
  onOpenItem: (item: ContentBankItemResponse) => void;
  onPublishNow: (item: ContentBankItemResponse) => void;
  onApprove: (item: ContentBankItemResponse) => void;
  onReject: (item: ContentBankItemResponse, reason: string) => void;
  onStage: (item: ContentBankItemResponse) => void;
  onDownload: (item: ContentBankItemResponse) => void;
  token: string;
}

/**
 * Editorial + publication actions for one card. The Bank is where an operator triages in bulk, so
 * the whole lifecycle has to be reachable here — previously the card rendered a state pill and the
 * only way to move an item was to open it in Content Studio one at a time.
 */
function ItemActions({
  item,
  busy,
  onApprove,
  onReject,
  onStage,
  onPublishNow,
  onDownload,
}: {
  item: ContentBankItemResponse;
  busy: boolean;
  onApprove: () => void;
  onReject: (reason: string) => void;
  onStage: () => void;
  onPublishNow: () => void;
  onDownload: () => void;
}) {
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState("");
  const { canApprove, canReject, canStage } = reviewActionsFor(item.state);
  const canPublish = canPublishItem(item.state, item.isPublishable);

  if (rejecting) {
    return (
      <div className="flex flex-col gap-1.5">
        <Input
          autoFocus
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Rejection reason"
          className="h-7 text-xs"
        />
        <div className="flex gap-1.5">
          <Button
            size="sm"
            variant="destructive"
            className="h-7 flex-1 text-xs"
            disabled={busy || reason.trim().length === 0}
            onClick={() => {
              onReject(reason.trim());
              setRejecting(false);
              setReason("");
            }}
          >
            Confirm
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="h-7 text-xs"
            onClick={() => {
              setRejecting(false);
              setReason("");
            }}
          >
            Cancel
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-wrap gap-1.5">
      {canApprove ? (
        <Button size="sm" variant="secondary" className="h-7 px-2 text-xs" disabled={busy} onClick={onApprove}>
          <Check className="h-3.5 w-3.5" /> Approve
        </Button>
      ) : null}
      {canReject ? (
        <Button
          size="sm"
          variant="outline"
          className="h-7 px-2 text-xs"
          disabled={busy}
          onClick={() => setRejecting(true)}
        >
          <X className="h-3.5 w-3.5" /> Reject
        </Button>
      ) : null}
      {canStage ? (
        <Button size="sm" variant="warning" className="h-7 px-2 text-xs" disabled={busy} onClick={onStage}>
          <SendHorizonal className="h-3.5 w-3.5" /> Mark ready
        </Button>
      ) : null}
      {canPublish ? (
        <Button size="sm" variant="default" className="h-7 px-2 text-xs" disabled={busy} onClick={onPublishNow}>
          <Send className="h-3.5 w-3.5" /> Publish now
        </Button>
      ) : null}
      {!item.isPublishable ? (
        // Script/copy deliverables have no network API to post to — the only meaningful delivery is
        // handing the operator the file.
        <Button size="sm" variant="outline" className="h-7 px-2 text-xs" disabled={busy} onClick={onDownload}>
          <Download className="h-3.5 w-3.5" /> Download
        </Button>
      ) : null}
    </div>
  );
}

/**
 * Readable preview for a text/script item, mirroring what Content Studio shows in its detail pane.
 * Script items (reel-script) previously fell through to a bare file icon plus a 90-character slice
 * of raw JSON, which told the operator nothing about the piece.
 */
function TextPreview({ item }: { item: ContentBankItemResponse }) {
  const body = item.text?.trim() || item.caption?.trim() || "";
  const lines = body ? textPreviewLines(body) : [];

  if (lines.length === 0) {
    return (
      <div className="flex h-full w-full flex-col items-center justify-center gap-1.5 p-3 text-center">
        <FileText className="h-5 w-5 text-muted-foreground" />
        <span className="text-[11px] text-muted-foreground">{item.formatKey}</span>
      </div>
    );
  }

  return (
    <div className="flex h-full w-full flex-col gap-1 overflow-hidden p-2.5 text-left">
      <span className="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        <FileText className="h-3 w-3" /> {item.formatKey}
      </span>
      <div className="flex flex-col gap-0.5 overflow-hidden">
        {lines.slice(0, 5).map((line, index) => (
          <p key={index} className="truncate text-[11px] leading-snug text-foreground/80">
            {line}
          </p>
        ))}
      </div>
      {lines.length > 5 ? (
        <span className="text-[10px] text-muted-foreground">+{lines.length - 5} more</span>
      ) : null}
    </div>
  );
}

/**
 * Renders an item's thumbnail: the resolved markup/background image when one exists, otherwise the
 * first raw asset (fetched the same way Content Studio's `AssetFallbackImage` does), otherwise a
 * graceful text/format preview instead of a broken-image icon.
 */
function ItemThumbnail({ item, token }: { item: ContentBankItemResponse; token: string }) {
  const markupThumbnail = resolveBankThumbnail(item.visualSpecMarkup);
  const [assetUrl, setAssetUrl] = useState<string | null>(null);
  // Text items render their body, never an asset: a script's stored asset is a .md/.txt file, and
  // pointing an <img>/background at it yields a broken image rather than a preview.
  const isText = item.medium === "Text";
  const firstAsset = isText ? undefined : item.assets[0];

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

  const thumbnail = isText ? null : markupThumbnail ?? assetUrl;

  if (thumbnail) {
    return (
      <div
        className="h-full w-full bg-cover bg-center"
        style={{ backgroundImage: `url(${thumbnail})` }}
        aria-hidden="true"
      />
    );
  }

  return <TextPreview item={item} />;
}

/** Bank tab (design §10): catalog/grid of item-cards across all sets/origins for the client. */
export function ContentBankGrid({
  items,
  loading,
  busy = false,
  fromUtc,
  toUtc,
  onRangeChange,
  stateFilter,
  onStateFilterChange,
  onOpenItem,
  onPublishNow,
  onApprove,
  onReject,
  onStage,
  onDownload,
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
                  <ItemActions
                    item={item}
                    busy={busy}
                    onApprove={() => onApprove(item)}
                    onReject={(reason) => onReject(item, reason)}
                    onStage={() => onStage(item)}
                    onPublishNow={() => onPublishNow(item)}
                    onDownload={() => onDownload(item)}
                  />
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
