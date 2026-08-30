"use client";

import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, Check, Download, PanelsTopLeft, Send, SendHorizonal, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { DateField } from "@/components/ui/date-field";
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog";
import { cn } from "@/lib/utils";
import { ReviewStateBadge } from "@/components/social/publications/publication-status-badge";
import type { ContentBankItemResponse } from "@/lib/api/publications";
import type { ContentReviewState } from "@/lib/api/content-studio";
import { useTheme } from "@/components/theme/theme-provider";
import { formatUserDate } from "@/lib/format/date";
import { canPublishItem, isOrphanedItem, reviewActionsFor } from "@/lib/publications/review-actions";
import { ItemThumbnail } from "@/components/social/publications/item-thumbnail";
import { formatLabel } from "@/lib/format/format-key";

const STATE_FILTERS: ContentReviewState[] = [
  "Draft",
  "InReview",
  "Edited",
  "Approved",
  "Rejected",
  "ReadyForPublication",
  // Terminal, but it must still be reachable: a HandedOff piece was previously visible only under
  // the "All" filter, which read as the piece having disappeared.
  "HandedOff",
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
  /** Permanently purges one piece and everything related to it. */
  onDelete: (item: ContentBankItemResponse) => void;
  /** Permanently purges a whole selection in a single round-trip. */
  onDeleteMany: (itemIds: string[]) => void;
  /** Opens a whole selection in ONE Content Studio session (its left rail lists every piece). */
  onOpenMany: (itemIds: string[]) => void;
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
  onDelete,
}: {
  item: ContentBankItemResponse;
  busy: boolean;
  onApprove: () => void;
  onReject: (reason: string) => void;
  onStage: () => void;
  onPublishNow: () => void;
  onDownload: () => void;
  onDelete: () => void;
}) {
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState("");
  const { canApprove, canReject, canStage } = reviewActionsFor(item.state);
  const canPublish = canPublishItem(item.state, item.isPublishable);
  const orphaned = isOrphanedItem(item);

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
      {orphaned ? (
        // An orphan cannot be opened, reviewed or fixed. Offering the editorial buttons here would
        // just be theater, so the card collapses to the one action that resolves it.
        <Button size="sm" variant="destructive" className="h-7 px-2 text-xs" disabled={busy} onClick={onDelete}>
          <Trash2 className="h-3.5 w-3.5" /> Delete permanently
        </Button>
      ) : null}
      {!orphaned && canApprove ? (
        <Button size="sm" variant="secondary" className="h-7 px-2 text-xs" disabled={busy} onClick={onApprove}>
          <Check className="h-3.5 w-3.5" /> Approve
        </Button>
      ) : null}
      {!orphaned && canReject ? (
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
      {!orphaned && canStage ? (
        // Approved → ReadyForPublication. Safe again now that staging no longer runs the legacy
        // handoff that ended in the terminal HandedOff state.
        <Button size="sm" variant="warning" className="h-7 px-2 text-xs" disabled={busy} onClick={onStage}>
          <SendHorizonal className="h-3.5 w-3.5" /> Mark ready
        </Button>
      ) : null}
      {!orphaned && canPublish ? (
        <Button size="sm" variant="default" className="h-7 px-2 text-xs" disabled={busy} onClick={onPublishNow}>
          <Send className="h-3.5 w-3.5" /> Publish now
        </Button>
      ) : null}
      {!orphaned && !item.isPublishable ? (
        // Script/copy deliverables have no network API to post to — the only meaningful delivery is
        // handing the operator the file.
        <Button size="sm" variant="outline" className="h-7 px-2 text-xs" disabled={busy} onClick={onDownload}>
          <Download className="h-3.5 w-3.5" /> Download
        </Button>
      ) : null}
      {!orphaned ? (
        <Button
          size="sm"
          variant="ghost"
          className="h-7 px-2 text-xs text-destructive hover:bg-destructive/10"
          disabled={busy}
          onClick={onDelete}
          title="Delete permanently"
          aria-label="Delete permanently"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </Button>
      ) : null}
    </div>
  );
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
  onDelete,
  onDeleteMany,
  onOpenMany,
  token,
}: ContentBankGridProps) {
  const { dateFormat } = useTheme();
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [pendingDelete, setPendingDelete] = useState<
    { kind: "one"; item: ContentBankItemResponse } | { kind: "many"; ids: string[] } | null
  >(null);

  const visibleIds = useMemo(() => items.map((i) => i.id), [items]);
  const rejectedIds = useMemo(() => items.filter((i) => i.state === "Rejected").map((i) => i.id), [items]);
  const orphanedIds = useMemo(() => items.filter(isOrphanedItem).map((i) => i.id), [items]);
  const allSelected = visibleIds.length > 0 && selectedIds.length === visibleIds.length;

  // A reload (filter change, or the refresh that follows a purge) can drop items that are still in
  // the selection. Keeping their ids would send a bulk delete for pieces the operator can no longer
  // see, so the selection is always narrowed to what is on screen.
  useEffect(() => {
    setSelectedIds((current) => current.filter((id) => visibleIds.includes(id)));
  }, [visibleIds]);

  function toggleSelected(itemId: string) {
    setSelectedIds((current) =>
      current.includes(itemId) ? current.filter((id) => id !== itemId) : [...current, itemId],
    );
  }

  function selectAll(ids: string[]) {
    setSelectedIds((current) => Array.from(new Set([...current, ...ids])));
  }

  const confirmation = pendingDelete
    ? pendingDelete.kind === "one"
      ? {
          title: "Delete this piece permanently?",
          description:
            "The piece, its stored assets, its set membership and its publication records are removed from the system. This cannot be undone. A piece that is scheduled or already published is refused.",
          confirm: () => onDelete(pendingDelete.item),
        }
      : {
          title: `Delete ${pendingDelete.ids.length} piece${pendingDelete.ids.length === 1 ? "" : "s"} permanently?`,
          description:
            "Every selected piece is removed from the system along with its stored assets, set membership and publication records. This cannot be undone. Pieces that are scheduled or already published are refused and stay in the Bank.",
          confirm: () => onDeleteMany(pendingDelete.ids),
        }
    : null;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-xs text-muted-foreground">
          From
          <DateField
            aria-label="From date"
            value={fromUtc.slice(0, 10)}
            onChange={(next) => onRangeChange(new Date(next).toISOString(), toUtc)}
            className="w-40"
          />
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted-foreground">
          To
          <DateField
            aria-label="To date"
            value={toUtc.slice(0, 10)}
            onChange={(next) => onRangeChange(fromUtc, new Date(next).toISOString())}
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

      {!loading && items.length > 0 ? (
        <div className="flex flex-wrap items-center gap-2 rounded-lg border border-border bg-muted/20 px-3 py-2">
          <span className="text-xs text-muted-foreground">
            {selectedIds.length === 0 ? "Nothing selected" : `${selectedIds.length} selected`}
          </span>
          <div className="flex flex-wrap gap-1.5">
            <Button
              size="sm"
              variant="outline"
              className="h-7 px-2 text-xs"
              disabled={busy}
              onClick={() => (allSelected ? setSelectedIds([]) : selectAll(visibleIds))}
            >
              {allSelected ? "Deselect all" : `Select all (${visibleIds.length})`}
            </Button>
            <Button
              size="sm"
              variant="outline"
              className="h-7 px-2 text-xs"
              disabled={busy || rejectedIds.length === 0}
              onClick={() => selectAll(rejectedIds)}
            >
              Select rejected ({rejectedIds.length})
            </Button>
            <Button
              size="sm"
              variant="outline"
              className="h-7 px-2 text-xs"
              disabled={busy || orphanedIds.length === 0}
              onClick={() => selectAll(orphanedIds)}
            >
              Select orphaned ({orphanedIds.length})
            </Button>
            {selectedIds.length > 0 ? (
              <>
                <Button
                  size="sm"
                  variant="default"
                  className="h-7 px-2 text-xs"
                  disabled={busy || selectedIds.every((id) => orphanedIds.includes(id))}
                  onClick={() => onOpenMany(selectedIds)}
                  title="Review every selected piece in one Studio session"
                >
                  <PanelsTopLeft className="h-3.5 w-3.5" /> Open in Studio
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-7 px-2 text-xs"
                  disabled={busy}
                  onClick={() => setSelectedIds([])}
                >
                  Clear
                </Button>
                <Button
                  size="sm"
                  variant="destructive"
                  className="h-7 px-2 text-xs"
                  disabled={busy}
                  onClick={() => setPendingDelete({ kind: "many", ids: selectedIds })}
                >
                  <Trash2 className="h-3.5 w-3.5" /> Delete selected
                </Button>
              </>
            ) : null}
          </div>
        </div>
      ) : null}

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
            const orphaned = isOrphanedItem(item);
            const selected = selectedIds.includes(item.id);
            return (
              <div
                key={item.id}
                className={cn(
                  "group flex flex-col overflow-hidden rounded-lg border bg-background shadow-sm",
                  selected ? "border-primary ring-1 ring-primary" : "border-border",
                  orphaned && !selected ? "border-amber-500/60" : null,
                )}
              >
                {/* `overflow-hidden` is load-bearing, not cosmetic: `aspect-square` only sets a
                    PREFERRED height, and a flex child refuses to shrink below its content, so the
                    scrolling script preview (which renders its lines twice) stretched the tile and
                    every card grew with it. Clipping here pins the tile to its square. */}
                <div className="relative flex aspect-square items-center justify-center overflow-hidden bg-muted/40">
                  <button
                    type="button"
                    onClick={() => onOpenItem(item)}
                    className="flex h-full w-full items-center justify-center"
                  >
                    <ItemThumbnail item={item} token={token} />
                  </button>
                  <label
                    className="absolute left-1.5 top-1.5 flex cursor-pointer items-center rounded bg-background/80 p-1 shadow-sm"
                    title="Select for bulk actions"
                  >
                    <Checkbox
                      checked={selected}
                      disabled={busy}
                      onChange={() => toggleSelected(item.id)}
                      aria-label={`Select ${item.formatKey}`}
                    />
                  </label>
                </div>
                <div className="flex flex-col gap-2 p-2">
                  <div className="flex items-center justify-between gap-1">
                    <span className="truncate text-xs font-medium">{formatLabel(item.formatKey)}</span>
                    <ReviewStateBadge state={item.state} />
                  </div>
                  {orphaned ? (
                    // The Bank is the only place this is visible: Studio just refuses to open the
                    // piece, which read as a bug rather than as a broken piece.
                    <span className="flex items-center gap-1 text-[11px] font-medium text-amber-600 dark:text-amber-400">
                      <AlertTriangle className="h-3 w-3 shrink-0" />
                      Orphaned — no content set, cannot be opened
                    </span>
                  ) : null}
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
                    onDelete={() => setPendingDelete({ kind: "one", item })}
                  />
                </div>
              </div>
            );
          })}
        </div>
      )}

      <ConfirmationDialog
        open={confirmation !== null}
        title={confirmation?.title ?? ""}
        description={confirmation?.description ?? ""}
        confirmLabel="Delete permanently"
        confirmVariant="destructive"
        onConfirm={() => {
          confirmation?.confirm();
          setPendingDelete(null);
        }}
        onCancel={() => setPendingDelete(null)}
      />
    </div>
  );
}
