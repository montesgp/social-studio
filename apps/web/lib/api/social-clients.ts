import { apiRequest } from "@/lib/api/client";
import { BackgroundSourcingPolicy, PageResult, SocialClient } from "@/lib/api/types";

export interface SocialClientsQuery {
  organizationId?: string;
  name?: string;
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
}

export interface SocialClientPayload {
  name: string;
  description?: string | null;
  sectorOrNiche?: string | null;
  backgroundSourcingPolicy?: BackgroundSourcingPolicy;
  /** Per-client aesthetic refresh staleness threshold in days. Null/undefined = system default (30 days). */
  aestheticRefreshThresholdDays?: number | null;
  /** Cap on how many pieces one generation job builds. Null/undefined = no cap. */
  maxPiecesPerGeneration?: number | null;
  /** Issue #70: whether this client signs their own slides with an @handle attribution. Plain boolean, default false (not signed). */
  signsOwnSlides?: boolean;
}

export interface DeletedSocialClient {
  id: string;
  organizationId: string;
  name: string;
}

export async function listSocialClients(query: SocialClientsQuery, token?: string) {
  const params = new URLSearchParams();
  if (query.organizationId) params.set("organizationId", query.organizationId);
  if (query.name) params.set("name", query.name);
  if (query.includeInactive) params.set("includeInactive", "true");
  params.set("page", String(query.page ?? 1));
  params.set("pageSize", String(query.pageSize ?? 20));
  return apiRequest<PageResult<SocialClient>>(`/api/social/clients?${params.toString()}`, undefined, token);
}

export async function getSocialClient(id: string, token?: string) {
  return apiRequest<SocialClient>(`/api/social/clients/${id}`, undefined, token);
}

export async function createSocialClient(payload: SocialClientPayload, token?: string) {
  return apiRequest<SocialClient>(
    "/api/social/clients",
    { method: "POST", body: JSON.stringify(payload) },
    token
  );
}

export async function updateSocialClient(id: string, payload: SocialClientPayload, token?: string) {
  return apiRequest<SocialClient>(
    `/api/social/clients/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token
  );
}

export async function deactivateSocialClient(id: string, token?: string) {
  return apiRequest<SocialClient>(`/api/social/clients/${id}/deactivate`, { method: "POST" }, token);
}

export async function activateSocialClient(id: string, token?: string) {
  return apiRequest<SocialClient>(`/api/social/clients/${id}/activate`, { method: "POST" }, token);
}

export async function deleteSocialClient(id: string, token?: string) {
  return apiRequest<DeletedSocialClient>(`/api/social/clients/${id}`, { method: "DELETE" }, token);
}
