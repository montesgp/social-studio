"use client";

import type { ReactNode } from "react";
import { Send } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ItemThumbnail } from "@/components/social/publications/item-thumbnail";
import { ReviewStateBadge } from "@/components/social/publications/publication-status-badge";
import { PublicationCard } from "@/components/social/publications/publication-card";
import { projectPlannerBoard, type PlannerItemCard, type PlannerPublicationCard } from "@/lib/publications/planner-board";
import type { ContentBankItemResponse, PublicationSummaryDto } from "@/lib/api/publications";

interface PublicationPlannerBoardProps {
  items: ContentBankItemResponse[];
  token: string;
  busy?: boolean;
  loading?: boolean;
  showCancelled: boolean;
  onOpenItem: (item: ContentBankItemResponse) => void;
  onPublishNow: (item: ContentBankItemResponse) => void;
  onRetry: (item: ContentBankItemResponse, publication: PublicationSummaryDto) => void;
  onCancel: (item: ContentBankItemResponse, publication: PublicationSummaryDto) => void;
}

interface ColumnProps {
  title: string;
  count: number;
  children: ReactNode;
}

function Column({ title, count, children }: ColumnProps) {
  return (
    <div className="flex min-w-[240px] flex-1 flex-col gap-2">
      <div className="flex items-center justify-between px-1">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{title}</h3>
        <span className="text-xs text-muted-foreground">{count}</span>
      </div>
      <div className="flex flex-1 flex-col gap-2 rounded-lg border border-dashed border-border bg-muted/10 p-2">
        {count === 0 ? <p className="p-2 text-center text-xs text-muted-foreground">Empty</p> : children}
      </div>
    </div>
  );
}

function ItemCardView({
  card,
  token,
  onOpen,
  onPublishNow,
}: {
  card: PlannerItemCard;
  token: string;
  onOpen: () => void;
  onPublishNow: () => void;
}) {
  return (
    <div className="flex flex-col gap-2 rounded-lg border border-border bg-background p-3 text-sm shadow-sm">
      <div className="flex items-start gap-2">
        {/* Small but real preview: the board decides what gets published, and a bare format key
            gave the operator no way to tell one carousel from another. */}
        <button
          type="button"
          onClick={onOpen}
          className="h-12 w-12 shrink-0 overflow-hidden rounded border border-border bg-muted/40"
          title="Open in Studio"
        >
          <ItemThumbnail item={card.item} token={token} compact />
        </button>
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <button type="button" onClick={onOpen} className="truncate text-left font-medium hover:underline">
            {card.item.formatKey}
          </button>
          <ReviewStateBadge state={card.item.state} />
        </div>
      </div>
      {card.item.isPublishable ? (
        <Button size="sm" variant="default" onClick={onPublishNow}>
          <Send className="h-3.5 w-3.5" /> Publish now
        </Button>
      ) : (
        // Script/copy deliverables reach "Ready" like any other piece, but there is no network API
        // to post them to — offering Publish here would just surface a server-side rejection.
        <p className="rounded-md border border-dashed border-border px-2 py-1 text-[11px] text-muted-foreground">
          Script deliverable — download only
        </p>
      )}
    </div>
  );
}

/**
 * Planner tab (design §2/OQ-3): pure projection kanban over (editorial ReviewState x publication
 * PublicationStatus). Never owns state — every action issues a command through the passed callbacks.
 */
export function PublicationPlannerBoard({
  items,
  token,
  busy,
  loading = false,
  showCancelled,
  onOpenItem,
  onPublishNow,
  onRetry,
  onCancel,
}: PublicationPlannerBoardProps) {
  // The board is a projection of the same fetch the Bank uses; while it is in flight every column
  // would otherwise read "Empty", which is indistinguishable from a client that has no content.
  if (loading) {
    return (
      <div className="flex flex-wrap gap-3 overflow-x-auto pb-2">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="flex min-w-[220px] flex-1 flex-col gap-2">
            <div className="h-4 w-24 animate-pulse rounded bg-muted/40" />
            <div className="h-40 animate-pulse rounded-lg border border-dashed border-border bg-muted/20" />
          </div>
        ))}
      </div>
    );
  }

  const board = projectPlannerBoard(items);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-3 overflow-x-auto pb-2">
        <Column title="Ready" count={board.ready.length}>
          {board.ready.map((card) => (
            <ItemCardView
              key={card.item.id}
              card={card}
              token={token}
              onOpen={() => onOpenItem(card.item)}
              onPublishNow={() => onPublishNow(card.item)}
            />
          ))}
        </Column>
        <Column title="Handed off" count={board.handedOff.length}>
          {board.handedOff.map((card) => (
            <ItemCardView
              key={card.item.id}
              card={card}
              token={token}
              onOpen={() => onOpenItem(card.item)}
              onPublishNow={() => onPublishNow(card.item)}
            />
          ))}
        </Column>
        <Column title="Scheduled" count={board.scheduled.length}>
          {board.scheduled.map((card) => (
            <PublicationCardView key={card.publication.id} card={card} busy={busy} onOpenItem={onOpenItem} onCancel={onCancel} />
          ))}
        </Column>
        <Column title="Publishing" count={board.publishing.length}>
          {board.publishing.map((card) => (
            <PublicationCardView key={card.publication.id} card={card} busy={busy} onOpenItem={onOpenItem} />
          ))}
        </Column>
        <Column title="Published" count={board.published.length}>
          {board.published.map((card) => (
            <PublicationCardView key={card.publication.id} card={card} busy={busy} onOpenItem={onOpenItem} />
          ))}
        </Column>
        <Column title="Failed" count={board.failed.length}>
          {board.failed.map((card) => (
            <PublicationCardView key={card.publication.id} card={card} busy={busy} onOpenItem={onOpenItem} onRetry={onRetry} onCancel={onCancel} />
          ))}
        </Column>
        {showCancelled ? (
          <Column title="Cancelled" count={board.cancelled.length}>
            {board.cancelled.map((card) => (
              <PublicationCardView key={card.publication.id} card={card} busy={busy} onOpenItem={onOpenItem} />
            ))}
          </Column>
        ) : null}
      </div>
    </div>
  );
}

function PublicationCardView({
  card,
  busy,
  onOpenItem,
  onRetry,
  onCancel,
}: {
  card: PlannerPublicationCard;
  busy?: boolean;
  onOpenItem: (item: ContentBankItemResponse) => void;
  onRetry?: (item: ContentBankItemResponse, publication: PublicationSummaryDto) => void;
  onCancel?: (item: ContentBankItemResponse, publication: PublicationSummaryDto) => void;
}) {
  return (
    <PublicationCard
      item={card.item}
      publication={card.publication}
      busy={busy}
      onOpen={() => onOpenItem(card.item)}
      onRetry={onRetry ? () => onRetry(card.item, card.publication) : undefined}
      onCancel={onCancel ? () => onCancel(card.item, card.publication) : undefined}
    />
  );
}
