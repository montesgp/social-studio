"use client";

import { ExternalLink, RotateCcw, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PublicationStatusBadge } from "@/components/social/publications/publication-status-badge";
import { AttemptHistoryPopover } from "@/components/social/publications/attempt-history-popover";
import type { ContentBankItemResponse, PublicationResponse, PublicationSummaryDto } from "@/lib/api/publications";

interface PublicationCardProps {
  item: ContentBankItemResponse;
  publication: PublicationSummaryDto;
  busy?: boolean;
  onOpen?: () => void;
  onRetry?: () => void;
  onCancel?: () => void;
  /** Attempts are fetched on demand (per-publication detail) — pass them once loaded. */
  detail?: PublicationResponse | null;
}

/** One card per (ContentItem, destination) publication (design OQ-3). */
export function PublicationCard({ item, publication, busy, onOpen, onRetry, onCancel, detail }: PublicationCardProps) {
  const canRetry = publication.status === "PublishFailed" && onRetry;
  const canCancel = (publication.status === "Scheduled" || publication.status === "PublishFailed") && onCancel;

  return (
    <div className="flex flex-col gap-2 rounded-lg border border-border bg-background p-3 text-sm shadow-sm">
      <div className="flex items-start justify-between gap-2">
        <button type="button" onClick={onOpen} className="truncate text-left font-medium hover:underline">
          {item.formatKey}
        </button>
        <PublicationStatusBadge status={publication.status} />
      </div>
      <p className="truncate text-xs text-muted-foreground">
        {publication.destination.networkKey} · attempt {publication.attemptCount}
      </p>
      {publication.externalReference ? (
        <a
          href={publication.externalReference}
          target="_blank"
          rel="noreferrer"
          className="inline-flex items-center gap-1 truncate text-xs text-primary hover:underline"
        >
          <ExternalLink className="h-3 w-3" /> {publication.externalReference}
        </a>
      ) : null}
      {detail ? <AttemptHistoryPopover attempts={detail.attempts} /> : null}
      {canRetry || canCancel ? (
        <div className="flex flex-wrap gap-1.5 pt-1">
          {canRetry ? (
            <Button size="sm" variant="secondary" disabled={busy} onClick={onRetry}>
              <RotateCcw className="h-3.5 w-3.5" /> Retry
            </Button>
          ) : null}
          {canCancel ? (
            <Button size="sm" variant="ghost" disabled={busy} onClick={onCancel}>
              <XCircle className="h-3.5 w-3.5" /> Cancel
            </Button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
