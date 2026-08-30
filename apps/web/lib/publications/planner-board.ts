import type { ContentBankItemResponse, PublicationSummaryDto } from "@/lib/api/publications";

/** An item-card: a `ContentItem` with no `Publication` yet, placed by its editorial `ReviewState`. */
export interface PlannerItemCard {
  kind: "item";
  item: ContentBankItemResponse;
}

/** A publication-card: one per (item, destination), placed by its own `PublicationStatus`. */
export interface PlannerPublicationCard {
  kind: "publication";
  item: ContentBankItemResponse;
  publication: PublicationSummaryDto;
}

export type PlannerCard = PlannerItemCard | PlannerPublicationCard;

export interface PlannerBoard {
  ready: PlannerItemCard[];
  /**
   * Editorially terminal items that carry no publication. Without this column a HandedOff piece
   * vanished from the Planner AND from Published — the operator saw it only under the Bank's "All"
   * filter, with no way to tell what had happened to it.
   */
  handedOff: PlannerItemCard[];
  scheduled: PlannerPublicationCard[];
  publishing: PlannerPublicationCard[];
  published: PlannerPublicationCard[];
  failed: PlannerPublicationCard[];
  /** Hidden by default (design OQ-3) — surfaced only via the "show cancelled" filter chip. */
  cancelled: PlannerPublicationCard[];
}

/** Only these editorial states may enter the "Ready" column as an actionable item-card (design OQ-3). */
export const PLANNER_READY_STATES = new Set(["Approved", "ReadyForPublication"]);

function emptyBoard(): PlannerBoard {
  return { ready: [], handedOff: [], scheduled: [], publishing: [], published: [], failed: [], cancelled: [] };
}

/**
 * Projects the Content Bank items for a client into the Planner kanban (design §2/OQ-3). Pure read
 * projection — never mutates state, only groups cards by column. An item with N publications
 * renders as N independent publication-cards (multi-destination fan-out); the item-card disappears
 * once at least one publication exists.
 */
export function projectPlannerBoard(items: ContentBankItemResponse[]): PlannerBoard {
  const board = emptyBoard();

  for (const item of items) {
    if (item.publications.length === 0) {
      if (PLANNER_READY_STATES.has(item.state)) {
        board.ready.push({ kind: "item", item });
      } else if (item.state === "HandedOff") {
        board.handedOff.push({ kind: "item", item });
      }
      continue;
    }

    for (const publication of item.publications) {
      const card: PlannerPublicationCard = { kind: "publication", item, publication };
      switch (publication.status) {
        case "Scheduled":
          board.scheduled.push(card);
          break;
        case "Publishing":
          board.publishing.push(card);
          break;
        case "Published":
          board.published.push(card);
          break;
        case "PublishFailed":
          board.failed.push(card);
          break;
        case "Cancelled":
          board.cancelled.push(card);
          break;
      }
    }
  }

  return board;
}
