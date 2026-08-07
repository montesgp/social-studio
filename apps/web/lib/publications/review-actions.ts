import type { ContentReviewState } from "@/lib/api/content-studio";

/**
 * Editorial states from which no further review action is possible.
 * Mirrors `TERMINAL` in `content-studio-panel.tsx`.
 */
export const TERMINAL_REVIEW_STATES: ContentReviewState[] = [
  "Discarded",
  "ReadyForPublication",
  "HandedOff",
];

/**
 * Which editorial transitions are offerable for an item, shared by the Content Studio detail pane
 * and the Content Bank cards so the two surfaces can never disagree about what is legal.
 *
 * These mirror the server-side guards; they only decide which affordances to RENDER. The API is
 * still the authority and rejects an illegal transition regardless of what the UI showed.
 */
export function reviewActionsFor(state: ContentReviewState) {
  const isTerminal = TERMINAL_REVIEW_STATES.includes(state);
  return {
    isTerminal,
    canApprove: !(["Approved", ...TERMINAL_REVIEW_STATES] as ContentReviewState[]).includes(state),
    canReject: !(["Rejected", ...TERMINAL_REVIEW_STATES] as ContentReviewState[]).includes(state),
    canDiscard: !(["Discarded", "ReadyForPublication", "HandedOff"] as ContentReviewState[]).includes(state),
    /** Staging is the explicit "ready to publish" step and is only legal straight from Approved. */
    canStage: state === "Approved",
  };
}

/**
 * Whether an item is eligible for a publish/schedule action on the publication axis.
 *
 * Two independent conditions, matching `PublicationEditorialGate` server-side:
 *  - the editorial axis must have reached Approved or ReadyForPublication, and
 *  - the format must be publishable at all (`isPublishable` is false for script/copy deliverables
 *    such as `reel-script`, which are download-only and have no network API to post to).
 */
export function canPublishItem(state: ContentReviewState, isPublishable: boolean): boolean {
  return isPublishable && (state === "Approved" || state === "ReadyForPublication");
}

/**
 * Whether a Bank piece is ORPHANED: it has no owning content set, so Content Studio cannot open it
 * and no editorial action on it is meaningful.
 *
 * `setId` is resolved server-side through set membership. A null means the membership row is
 * missing — pieces produced by the variant/duplicate bug fixed in `SpawnVariantCommand` and
 * `DuplicateItemCommand`, which that fix deliberately did not retro-adopt. The only resolution from
 * the UI is a permanent purge.
 */
export function isOrphanedItem(item: { setId: string | null }): boolean {
  return !item.setId;
}
