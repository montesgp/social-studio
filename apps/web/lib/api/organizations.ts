import { apiRequest } from "@/lib/api/client";
import { Organization, PageResult } from "@/lib/api/types";

export interface OrganizationsQuery {
  name?: string;
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
}

export interface OrganizationPayload {
  name: string;
}

export async function listOrganizations(query: OrganizationsQuery, token?: string) {
  const params = new URLSearchParams();
  if (query.name) params.set("name", query.name);
  if (query.includeInactive) params.set("includeInactive", "true");
  params.set("page", String(query.page ?? 1));
  params.set("pageSize", String(query.pageSize ?? 20));
  return apiRequest<PageResult<Organization>>(`/api/organizations?${params.toString()}`, undefined, token);
}

export async function getOrganization(id: string, token?: string) {
  return apiRequest<Organization>(`/api/organizations/${id}`, undefined, token);
}

export async function createOrganization(payload: OrganizationPayload, token?: string) {
  return apiRequest<Organization>(
    "/api/organizations",
    { method: "POST", body: JSON.stringify(payload) },
    token
  );
}

export async function updateOrganization(id: string, payload: OrganizationPayload, token?: string) {
  return apiRequest<Organization>(
    `/api/organizations/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token
  );
}

export async function deactivateOrganization(id: string, token?: string) {
  return apiRequest<Organization>(`/api/organizations/${id}/deactivate`, { method: "POST" }, token);
}

export async function activateOrganization(id: string, token?: string) {
  return apiRequest<Organization>(`/api/organizations/${id}/activate`, { method: "POST" }, token);
}
