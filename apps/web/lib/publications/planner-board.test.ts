import { test } from "node:test";
import assert from "node:assert/strict";
import { projectPlannerBoard } from "./planner-board.ts";
import type { ContentBankItemResponse, PublicationSummaryDto } from "../api/publications.ts";

function item(overrides: Partial<ContentBankItemResponse> = {}): ContentBankItemResponse {
  return {
    id: "item-1",
    organizationId: "org-1",
    clientId: "client-1",
    origin: { generatorKind: "video", sourceRunId: "run-1", sourcePackageId: null, formatResultId: "fr-1", formatKey: "post" },
    formatKey: "post",
    medium: "Image",
    text: null,
    caption: "caption",
    visualSpecMarkup: null,
    assets: [],
    targetNetwork: "instagram",
    targetPlacement: "feed",
    state: "Draft",
    editTrace: { lastEditedSource: "system", lastEditedAtUtc: null, promptVersion: null },
    variantGroupId: "vg-1",
    isPrimaryVariant: true,
    planContext: null,
    rejectionReason: null,
    reviewerUserId: null,
    createdAtUtc: "2026-07-10T00:00:00.000Z",
    updatedAtUtc: "2026-07-10T00:00:00.000Z",
    publications: [],
    setId: null,
    isPublishable: true,
    ...overrides,
  };
}

function publication(overrides: Partial<PublicationSummaryDto> = {}): PublicationSummaryDto {
  return {
    id: "pub-1",
    status: "Scheduled",
    destination: { clientAccountConnectionId: "conn-1", socialNetworkId: "network-1", networkKey: "instagram" },
    attemptCount: 0,
    externalReference: null,
    ...overrides,
  };
}

test("projectPlannerBoard: an Approved item with no publication lands as an item-card in Ready", () => {
  const board = projectPlannerBoard([item({ id: "a", state: "Approved" })]);

  assert.equal(board.ready.length, 1);
  assert.equal(board.ready[0].kind, "item");
  assert.equal(board.ready[0].item.id, "a");
});

test("projectPlannerBoard: a ReadyForPublication item with no publication also lands in Ready", () => {
  const board = projectPlannerBoard([item({ id: "b", state: "ReadyForPublication" })]);

  assert.equal(board.ready.length, 1);
});

test("projectPlannerBoard: a Draft item with no publication is excluded from every column", () => {
  const board = projectPlannerBoard([item({ id: "c", state: "Draft" })]);

  assert.equal(board.ready.length, 0);
  assert.equal(board.scheduled.length + board.publishing.length + board.published.length + board.failed.length, 0);
});

test("projectPlannerBoard: an item with a publication never shows an item-card, even if Approved", () => {
  const board = projectPlannerBoard([
    item({ id: "d", state: "Approved", publications: [publication({ status: "Publishing" })] }),
  ]);

  assert.equal(board.ready.length, 0);
  assert.equal(board.publishing.length, 1);
});

test("projectPlannerBoard: each publication status is routed to its own column", () => {
  const board = projectPlannerBoard([
    item({
      id: "e",
      state: "Approved",
      publications: [
        publication({ id: "p-scheduled", status: "Scheduled" }),
        publication({ id: "p-publishing", status: "Publishing" }),
        publication({ id: "p-published", status: "Published" }),
        publication({ id: "p-failed", status: "PublishFailed" }),
        publication({ id: "p-cancelled", status: "Cancelled" }),
      ],
    }),
  ]);

  assert.equal(board.scheduled.length, 1);
  assert.equal(board.publishing.length, 1);
  assert.equal(board.published.length, 1);
  assert.equal(board.failed.length, 1);
  assert.equal(board.cancelled.length, 1);
});

test("projectPlannerBoard: multi-destination items render as multiple independent publication-cards", () => {
  const board = projectPlannerBoard([
    item({
      id: "f",
      publications: [
        publication({ id: "p1", status: "Publishing", destination: { clientAccountConnectionId: "c1", socialNetworkId: "n1", networkKey: "instagram" } }),
        publication({ id: "p2", status: "Publishing", destination: { clientAccountConnectionId: "c2", socialNetworkId: "n1", networkKey: "instagram" } }),
      ],
    }),
  ]);

  assert.equal(board.publishing.length, 2);
  assert.notEqual(board.publishing[0].publication.id, board.publishing[1].publication.id);
});

test("a HandedOff item with no publication lands in the handedOff column, not nowhere", () => {
  // It used to fall through every column: invisible in Planner and in Published, visible only under
  // the Bank's "All" filter, which read as the piece having vanished.
  const board = projectPlannerBoard([item({ state: "HandedOff" })]);

  assert.equal(board.handedOff.length, 1);
  assert.equal(board.ready.length, 0);
});

test("HandedOff items with a publication still render as publication cards", () => {
  const board = projectPlannerBoard([
    item({
      state: "HandedOff",
      publications: [publication({ status: "Published" })],
    }),
  ]);

  assert.equal(board.handedOff.length, 0);
  assert.equal(board.published.length, 1);
});
