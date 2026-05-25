import { apiRequest } from "@/lib/api/client";
import { PageResult, SocialClient } from "@/lib/api/types";

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
