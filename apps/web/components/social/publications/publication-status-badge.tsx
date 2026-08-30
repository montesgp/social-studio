import { Badge, type BadgeVariant } from "@/components/ui/badge";
import type { PublicationStatus } from "@/lib/api/publications";
import type { ContentReviewState } from "@/lib/api/content-studio";

const PUBLICATION_STATUS_VARIANT: Record<PublicationStatus, BadgeVariant> = {
  Scheduled: "info",
  Publishing: "warning",
  Published: "success",
  PublishFailed: "destructive",
  Cancelled: "muted",
};

export function PublicationStatusBadge({ status }: { status: PublicationStatus }) {
  return <Badge variant={PUBLICATION_STATUS_VARIANT[status]}>{status}</Badge>;
}

const REVIEW_STATE_VARIANT: Record<ContentReviewState, BadgeVariant> = {
  Draft: "muted",
  InReview: "info",
  Edited: "warning",
  Approved: "success",
  Rejected: "destructive",
  Discarded: "muted",
  ReadyForPublication: "accent",
  HandedOff: "default",
};

export function ReviewStateBadge({ state }: { state: ContentReviewState }) {
  return <Badge variant={REVIEW_STATE_VARIANT[state]}>{state}</Badge>;
}
