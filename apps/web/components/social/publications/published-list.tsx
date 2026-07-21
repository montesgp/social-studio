"use client";

import { ExternalLink } from "lucide-react";
import { PublicationStatusBadge } from "@/components/social/publications/publication-status-badge";
import type { ContentBankItemResponse, PublicationSummaryDto } from "@/lib/api/publications";

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

interface PublishedRow {
  item: ContentBankItemResponse;
  publication: PublicationSummaryDto;
}

interface PublishedListProps {
  items: ContentBankItemResponse[];
  onOpenItem: (item: ContentBankItemResponse) => void;
}

/** Published tab (design §10): recency-weighted list of `Published` publications. */
export function PublishedList({ items, onOpenItem }: PublishedListProps) {
  const rows: PublishedRow[] = items
    .flatMap((item) => item.publications.map((publication) => ({ item, publication })))
    .filter((row) => row.publication.status === "Published")
    .sort((a, b) => b.item.updatedAtUtc.localeCompare(a.item.updatedAtUtc));

  if (rows.length === 0) {
    return (
      <div className="rounded-lg border border-border bg-muted/20 p-8 text-center text-sm text-muted-foreground">
        Nothing published in this range yet.
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      {rows.map(({ item, publication }) => (
        <div
          key={publication.id}
          className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-background p-3 text-sm shadow-sm"
        >
          <div className="flex flex-col gap-0.5">
            <button type="button" onClick={() => onOpenItem(item)} className="text-left font-medium hover:underline">
              {item.formatKey}
            </button>
            <span className="text-xs text-muted-foreground">
              {publication.destination.networkKey} · {formatDateTime(item.updatedAtUtc)}
            </span>
          </div>
          <div className="flex items-center gap-2">
            <PublicationStatusBadge status={publication.status} />
            {publication.externalReference ? (
              <a
                href={publication.externalReference}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1 text-xs text-primary hover:underline"
              >
                <ExternalLink className="h-3.5 w-3.5" /> View
              </a>
            ) : null}
          </div>
        </div>
      ))}
    </div>
  );
}
