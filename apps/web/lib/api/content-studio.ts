import { apiRequest } from "@/lib/api/client";
import { ACTIVE_ORG_STORAGE_KEY } from "@/lib/auth/session";

const base = "/api/social/content-studio";

export type ContentReviewState =
  | "Draft"
  | "InReview"
  | "Edited"
  | "Approved"
  | "Rejected"
  | "Discarded"
  | "ReadyForPublication"
  | "HandedOff";

export interface ContentOriginDto {
  generatorKind: string;
  sourceRunId: string;
  sourcePackageId: string | null;
  formatResultId: string;
  formatKey: string;
}

export interface ContentAssetDto {
  role: string;
  storageUri: string;
  mime: string;
}

export interface EditTraceDto {
  lastEditedSource: string;
  lastEditedAtUtc: string | null;
  promptVersion: string | null;
}

export interface PlanContextDto {
  contentPlanId: string;
  periodKind: number;
  year: number;
  month: number;
  calendarSlotId: string;
}

export interface ContentItem {
  id: string;
  organizationId: string;
  clientId: string;
  origin: ContentOriginDto;
  formatKey: string;
  medium: string;
  text: string | null;
  visualSpecMarkup: string | null;
  assets: ContentAssetDto[];
  targetNetwork: string;
  targetPlacement: string;
  state: ContentReviewState;
  editTrace: EditTraceDto;
  variantGroupId: string;
  isPrimaryVariant: boolean;
  planContext: PlanContextDto | null;
  rejectionReason: string | null;
  reviewerUserId: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ReviewSummary {
  total: number;
  draft: number;
  inReview: number;
  edited: number;
  approved: number;
  rejected: number;
  discarded: number;
  readyForPublication: number;
  handedOff: number;
}

export interface ContentSetMemberDto {
  itemId: string;
  sortOrder: number;
}

export interface ContentSet {
  id: string;
  organizationId: string;
  clientId: string;
  kind: string;
  sourceRunId: string | null;
  summary: ReviewSummary;
  members: ContentSetMemberDto[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ChannelConstraints {
  aspectRatio: string;
  maxCaptionLength: number;
  maxSlides: number;
}

export interface ContentPreview {
  itemId: string;
  medium: string;
  targetNetwork: string;
  targetPlacement: string;
  text: string | null;
  visualSpecMarkup: string | null;
  assets: ContentAssetDto[];
  constraints: ChannelConstraints;
}

export function materializeRun(jobId: string, clientId: string, token?: string): Promise<ContentSet> {
  return apiRequest<ContentSet>(
    `${base}/runs/${jobId}/materialize`,
    { method: "POST", body: JSON.stringify({ clientId }) },
    token,
  );
}

/** Materializes ALL formats a client generated from a single analysis (aggregates every generation job). */
export function materializeAnalysis(analysisId: string, clientId: string, token?: string): Promise<ContentSet> {
  return apiRequest<ContentSet>(
    `${base}/analyses/${analysisId}/materialize`,
    { method: "POST", body: JSON.stringify({ clientId }) },
    token,
  );
}

export function getContentSet(setId: string, token?: string): Promise<ContentSet> {
  return apiRequest<ContentSet>(`${base}/sets/${setId}`, undefined, token);
}

export function listContentItems(setId: string, state: string | undefined, token?: string): Promise<ContentItem[]> {
  const query = state ? `?state=${encodeURIComponent(state)}` : "";
  return apiRequest<ContentItem[]>(`${base}/sets/${setId}/items${query}`, undefined, token);
}

export function getContentItem(itemId: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(`${base}/items/${itemId}`, undefined, token);
}

export function getContentPreview(itemId: string, token?: string): Promise<ContentPreview> {
  return apiRequest<ContentPreview>(`${base}/items/${itemId}/preview`, undefined, token);
}

export function editItemText(itemId: string, text: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(
    `${base}/items/${itemId}/text`,
    { method: "PUT", body: JSON.stringify({ text }) },
    token,
  );
}

export function approveItem(itemId: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(`${base}/items/${itemId}/approve`, { method: "POST" }, token);
}

export function rejectItem(itemId: string, reason: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(
    `${base}/items/${itemId}/reject`,
    { method: "POST", body: JSON.stringify({ reason }) },
    token,
  );
}

export function discardItem(itemId: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(`${base}/items/${itemId}/discard`, { method: "POST" }, token);
}

export function duplicateItem(itemId: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(`${base}/items/${itemId}/duplicate`, { method: "POST" }, token);
}

export function deleteItem(itemId: string, token?: string): Promise<void> {
  return apiRequest<void>(`${base}/items/${itemId}`, { method: "DELETE" }, token);
}

export function stageItem(itemId: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(`${base}/items/${itemId}/stage`, { method: "POST" }, token);
}

export interface VisualAssetInput {
  role: string;
  storageUri: string;
  mime: string;
}

export function editItemVisual(
  itemId: string,
  visualSpecMarkup: string | null,
  assets: VisualAssetInput[],
  token?: string,
): Promise<ContentItem> {
  return apiRequest<ContentItem>(
    `${base}/items/${itemId}/visual`,
    { method: "PUT", body: JSON.stringify({ visualSpecMarkup, assets }) },
    token,
  );
}

export function spawnVariant(itemId: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(`${base}/items/${itemId}/variant`, { method: "POST" }, token);
}

export function linkItemToPlan(
  itemId: string,
  link: { contentPlanId: string; periodKind: number; year: number; month: number; calendarSlotId: string },
  token?: string,
): Promise<ContentItem> {
  return apiRequest<ContentItem>(
    `${base}/items/${itemId}/plan-link`,
    { method: "PUT", body: JSON.stringify(link) },
    token,
  );
}

export function unlinkItemFromPlan(itemId: string, token?: string): Promise<ContentItem> {
  return apiRequest<ContentItem>(`${base}/items/${itemId}/plan-link`, { method: "DELETE" }, token);
}

export type BulkOperation = "Approve" | "Reject" | "Discard" | "Delete";

export interface BulkItemOutcome {
  itemId: string;
  succeeded: boolean;
  errorCode: string | null;
}

export interface BulkOperationResult {
  total: number;
  succeeded: number;
  failed: number;
  outcomes: BulkItemOutcome[];
}

export function bulkOperation(
  itemIds: string[],
  operation: BulkOperation,
  reason: string | null,
  token?: string,
): Promise<BulkOperationResult> {
  return apiRequest<BulkOperationResult>(
    `${base}/items/bulk`,
    { method: "POST", body: JSON.stringify({ itemIds, operation, reason }) },
    token,
  );
}

/** Downloads the set's client-presentation PPTX deck (binary), carrying auth + tenant headers. */
export async function downloadPresentationPptx(setId: string, token?: string): Promise<void> {
  const headers: Record<string, string> = {};
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  if (typeof window !== "undefined") {
    const orgId = window.localStorage.getItem(ACTIVE_ORG_STORAGE_KEY);
    if (orgId) {
      headers["X-Tenant-Id"] = orgId;
    }
  }

  const response = await fetch(`/hive-api${base}/sets/${setId}/export.pptx`, { headers, cache: "no-store" });
  if (!response.ok) {
    throw { message: `Presentation export failed (HTTP ${response.status}).` };
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = "content-studio-presentation.pptx";
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}
