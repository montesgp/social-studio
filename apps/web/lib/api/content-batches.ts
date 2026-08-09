import { apiRequest } from "@/lib/api/client";

/**
 * Client for multi-piece batch generation.
 *
 * A batch is a cart of N pieces that runs in the background. Nothing here blocks: the POST returns
 * as soon as the pieces are queued, and progress is polled separately, so the operator can walk away
 * to any other module while the worker grinds.
 */

/** One tile in the composition cart. */
export interface BatchCompositionItem {
  formatKey: string;
  count: number;
}

export interface StartBatchPayload {
  clientId: string;
  composition: BatchCompositionItem[];
  /** The "surprise me x N" tile. Merges with the explicit tiles above. */
  assortedTotal?: number;
}

export interface StartBatchResponse {
  batchId: string;
  totalPieces: number;
  /** The RESOLVED mix, including whatever the assorted tile decided. */
  composition: BatchCompositionItem[];
  statusUrl: string;
}

export interface BatchProgress {
  batchId: string;
  status: string;
  totalPieces: number;
  completedPieces: number;
  failedPieces: number;
  runningPieces: number;
  queuedPieces: number;
  currentPieceIndex: number | null;
  currentFormatKey: string | null;
}

/** A catalog format the operator can drag into the cart. Mirrors FormatCatalogEntryDto. */
export interface ContentFormatOption {
  formatKey: string;
  surface: string;
  layout: string | null;
  medium: string;
  displayLabel: string;
  isMultiAsset: boolean;
  isAuxiliary: boolean;
}

export async function startContentBatch(
  analysisId: string,
  payload: StartBatchPayload,
  token?: string
) {
  return apiRequest<StartBatchResponse>(
    `/api/social/single-analyses/${analysisId}/batches`,
    {
      method: "POST",
      body: JSON.stringify({
        clientId: payload.clientId,
        composition: payload.composition,
        assortedTotal: payload.assortedTotal ?? null
      })
    },
    token
  );
}

export async function getBatchProgress(batchId: string, token?: string) {
  return apiRequest<BatchProgress>(
    `/api/social/content-generation-batches/${batchId}/progress`,
    undefined,
    token
  );
}

export async function listActiveBatches(token?: string) {
  return apiRequest<BatchProgress[]>(
    "/api/social/content-generation-batches/active",
    undefined,
    token
  );
}

/**
 * Deliverable formats, straight from the backend catalog.
 *
 * The cart NEVER hardcodes the tile list: a new catalog row must show up as a new tile without a
 * frontend deploy. Auxiliary entries (caption, hook, …) are ingredients of a piece, not pieces you
 * can order by the unit, so they are filtered out.
 */
export async function listDeliverableFormats(token?: string) {
  const response = await apiRequest<{ entries?: ContentFormatOption[] }>(
    "/api/social/content-generation/formats",
    undefined,
    token
  );

  return (response.entries ?? []).filter((format) => !format.isAuxiliary);
}
