import { apiRequest } from "@/lib/api/client";
import type {
  ContentAssetDto,
  ContentOriginDto,
  ContentReviewState,
  EditTraceDto,
  PlanContextDto,
} from "@/lib/api/content-studio";

const bankBase = "/api/social/content-bank";
const publicationBase = "/api/social/publications";
const socialBase = "/api/social";

export type PublicationStatus = "Scheduled" | "Publishing" | "Published" | "PublishFailed" | "Cancelled";

export interface PublicationDestinationDto {
  clientAccountConnectionId: string;
  socialNetworkId: string;
  networkKey: string;
}

export interface PublicationAttemptDto {
  attemptNumber: number;
  startedAtUtc: string;
  finishedAtUtc: string | null;
  outcome: string;
  errorMessage: string | null;
  externalReference: string | null;
}

/** Lightweight per-item publication summary attached to a Content Bank item (design §5.2). */
export interface PublicationSummaryDto {
  id: string;
  status: PublicationStatus;
  destination: PublicationDestinationDto;
  attemptCount: number;
  externalReference: string | null;
}

/** Full publication detail (design §9 — `GET /api/social/publications/{publicationId}`). */
export interface PublicationResponse {
  id: string;
  organizationId: string;
  clientId: string;
  contentItemId: string;
  destination: PublicationDestinationDto;
  status: PublicationStatus;
  scheduledForUtc: string | null;
  externalReference: string | null;
  attempts: PublicationAttemptDto[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

/** `ContentItemResponse` fields plus the publication axis (design §5.2). */
export interface ContentBankItemResponse {
  id: string;
  organizationId: string;
  clientId: string;
  origin: ContentOriginDto;
  formatKey: string;
  medium: string;
  text: string | null;
  caption: string | null;
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
  publications: PublicationSummaryDto[];
}

/** Token-free projection of a `ClientAccountConnection`. NEVER includes the access token. */
export interface ClientAccountConnectionResponse {
  id: string;
  clientId: string;
  socialNetworkId: string;
  organizationIntegrationId: string;
  externalAccountId: string;
  tokenExpiresAtUtc: string | null;
  status: "Connected" | "Failed" | "Disabled";
  lastTestedAtUtc: string | null;
  lastError: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ContentBankFilterQuery {
  fromUtc?: string;
  toUtc?: string;
  state?: string;
}

export function listContentBankItems(
  clientId: string,
  filter?: ContentBankFilterQuery,
  token?: string,
): Promise<ContentBankItemResponse[]> {
  const params = new URLSearchParams();
  if (filter?.fromUtc) params.set("fromUtc", filter.fromUtc);
  if (filter?.toUtc) params.set("toUtc", filter.toUtc);
  if (filter?.state) params.set("state", filter.state);
  const query = params.toString();
  return apiRequest<ContentBankItemResponse[]>(
    `${bankBase}/clients/${clientId}/items${query ? `?${query}` : ""}`,
    undefined,
    token,
  );
}

export interface PublishItemNowPayload {
  clientId: string;
  clientAccountConnectionId: string;
  socialNetworkId: string;
  networkKey: string;
}

export function publishItemNow(
  itemId: string,
  payload: PublishItemNowPayload,
  token?: string,
): Promise<PublicationResponse> {
  return apiRequest<PublicationResponse>(
    `${bankBase}/items/${itemId}/publish`,
    { method: "POST", body: JSON.stringify(payload) },
    token,
  );
}

export interface SchedulePublicationPayload extends PublishItemNowPayload {
  scheduledForUtc: string;
}

/** Domain-ready per design §15 — the v1 UI does not surface a "Schedule" action yet. */
export function scheduleItem(
  itemId: string,
  payload: SchedulePublicationPayload,
  token?: string,
): Promise<PublicationResponse> {
  return apiRequest<PublicationResponse>(
    `${bankBase}/items/${itemId}/schedule`,
    { method: "POST", body: JSON.stringify(payload) },
    token,
  );
}

export function getPublication(publicationId: string, token?: string): Promise<PublicationResponse> {
  return apiRequest<PublicationResponse>(`${publicationBase}/${publicationId}`, undefined, token);
}

export function retryPublication(publicationId: string, token?: string): Promise<PublicationResponse> {
  return apiRequest<PublicationResponse>(`${publicationBase}/${publicationId}/retry`, { method: "POST" }, token);
}

export function cancelPublication(publicationId: string, token?: string): Promise<PublicationResponse> {
  return apiRequest<PublicationResponse>(`${publicationBase}/${publicationId}/cancel`, { method: "POST" }, token);
}

export function reclaimPublication(
  publicationId: string,
  reason: string,
  token?: string,
): Promise<PublicationResponse> {
  return apiRequest<PublicationResponse>(
    `${publicationBase}/${publicationId}/reclaim`,
    { method: "POST", body: JSON.stringify({ reason }) },
    token,
  );
}

export function listClientAccountConnections(
  clientId: string,
  token?: string,
): Promise<ClientAccountConnectionResponse[]> {
  return apiRequest<ClientAccountConnectionResponse[]>(
    `${socialBase}/clients/${clientId}/account-connections`,
    undefined,
    token,
  );
}

export interface CreateClientAccountConnectionPayload {
  socialNetworkId: string;
  organizationIntegrationId: string;
  externalAccountId: string;
  accessToken: string;
  tokenExpiresAtUtc?: string | null;
}

export function createClientAccountConnection(
  clientId: string,
  payload: CreateClientAccountConnectionPayload,
  token?: string,
): Promise<ClientAccountConnectionResponse> {
  return apiRequest<ClientAccountConnectionResponse>(
    `${socialBase}/clients/${clientId}/account-connections`,
    { method: "POST", body: JSON.stringify(payload) },
    token,
  );
}

export interface RotateClientAccountConnectionTokenPayload {
  accessToken: string;
  tokenExpiresAtUtc?: string | null;
}

export function rotateClientAccountConnectionToken(
  connectionId: string,
  payload: RotateClientAccountConnectionTokenPayload,
  token?: string,
): Promise<ClientAccountConnectionResponse> {
  return apiRequest<ClientAccountConnectionResponse>(
    `${socialBase}/account-connections/${connectionId}/token`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}

export function testClientAccountConnection(
  connectionId: string,
  token?: string,
): Promise<ClientAccountConnectionResponse> {
  return apiRequest<ClientAccountConnectionResponse>(
    `${socialBase}/account-connections/${connectionId}/test`,
    { method: "POST" },
    token,
  );
}
