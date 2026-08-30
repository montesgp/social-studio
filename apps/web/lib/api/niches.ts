import { apiRequest } from "@/lib/api/client";
import { ClientNiches, Niche, PageResult } from "@/lib/api/types";

/**
 * The catalog list is bounded server-side: `DefaultPageSize = 20`, `MaxPageSize = 50`. With a
 * search term the API filters by display name/code and orders alphabetically; with no term it
 * returns the most recently created entries first.
 */
export const NICHE_DEFAULT_PAGE_SIZE = 20;
export const NICHE_MAX_PAGE_SIZE = 50;

export interface NichesQuery {
  /** Silently coerced to false by the API for non-PlatformAdmin callers. */
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
  searchTerm?: string;
}

export interface CreateNichePayload {
  code: string;
  displayName: string;
  iconKey: string | null;
  searchHashtags: string[];
}

export interface UpdateNichePayload {
  displayName: string;
  iconKey: string | null;
  searchHashtags: string[];
}

export async function listNiches(query: NichesQuery, token?: string) {
  const params = new URLSearchParams();
  if (query.includeInactive) params.set("includeInactive", "true");
  params.set("page", String(query.page ?? 1));
  params.set("pageSize", String(query.pageSize ?? NICHE_DEFAULT_PAGE_SIZE));
  if (query.searchTerm) params.set("searchTerm", query.searchTerm);
  return apiRequest<PageResult<Niche>>(`/api/social/niches?${params.toString()}`, undefined, token);
}

export async function getNiche(id: string, token?: string) {
  return apiRequest<Niche>(`/api/social/niches/${id}`, undefined, token);
}

export async function createNiche(payload: CreateNichePayload, token?: string) {
  return apiRequest<Niche>(
    "/api/social/niches",
    { method: "POST", body: JSON.stringify(payload) },
    token
  );
}

export async function updateNiche(id: string, payload: UpdateNichePayload, token?: string) {
  return apiRequest<Niche>(
    `/api/social/niches/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token
  );
}

export async function deactivateNiche(id: string, token?: string) {
  return apiRequest<Niche>(`/api/social/niches/${id}/deactivate`, { method: "POST" }, token);
}

export async function activateNiche(id: string, token?: string) {
  return apiRequest<Niche>(`/api/social/niches/${id}/activate`, { method: "POST" }, token);
}

export async function getClientNiches(clientId: string, token?: string, organizationId?: string) {
  return apiRequest<ClientNiches>(
    `/api/social/clients/${clientId}/niches`,
    undefined,
    token,
    organizationId
  );
}

/** Replaces the whole selection — the API has no add/remove, only replace. */
export async function assignClientNiches(
  clientId: string,
  nicheIds: string[],
  token?: string,
  organizationId?: string
) {
  return apiRequest<ClientNiches>(
    `/api/social/clients/${clientId}/niches`,
    { method: "PUT", body: JSON.stringify({ nicheIds }) },
    token,
    organizationId
  );
}
