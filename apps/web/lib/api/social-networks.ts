import { apiRequest } from "@/lib/api/client";
import { PageResult, SocialNetwork } from "@/lib/api/types";

export interface SocialNetworksQuery {
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
  searchTerm?: string;
}

export interface CreateSocialNetworkPayload {
  code: string;
  displayName: string;
  iconKey: string;
}

export interface UpdateSocialNetworkPayload {
  displayName: string;
  iconKey: string;
}

export async function listSocialNetworks(query: SocialNetworksQuery, token?: string) {
  const params = new URLSearchParams();
  if (query.includeInactive) params.set("includeInactive", "true");
  params.set("page", String(query.page ?? 1));
  params.set("pageSize", String(query.pageSize ?? 20));
  if (query.searchTerm) params.set("searchTerm", query.searchTerm);
  return apiRequest<PageResult<SocialNetwork>>(`/api/social/networks?${params.toString()}`, undefined, token);
}

export async function getSocialNetwork(id: string, token?: string) {
  return apiRequest<SocialNetwork>(`/api/social/networks/${id}`, undefined, token);
}

export async function createSocialNetwork(payload: CreateSocialNetworkPayload, token?: string) {
  return apiRequest<SocialNetwork>(
    "/api/social/networks",
    { method: "POST", body: JSON.stringify(payload) },
    token
  );
}

export async function updateSocialNetwork(id: string, payload: UpdateSocialNetworkPayload, token?: string) {
  return apiRequest<SocialNetwork>(
    `/api/social/networks/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token
  );
}

export async function deactivateSocialNetwork(id: string, token?: string) {
  return apiRequest<SocialNetwork>(`/api/social/networks/${id}/deactivate`, { method: "POST" }, token);
}

export async function activateSocialNetwork(id: string, token?: string) {
  return apiRequest<SocialNetwork>(`/api/social/networks/${id}/activate`, { method: "POST" }, token);
}
