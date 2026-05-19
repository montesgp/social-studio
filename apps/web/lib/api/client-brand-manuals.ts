import { apiRequest } from "@/lib/api/client";
import type {
  ClientBrandManual,
  CreateClientBrandManualPayload,
  UpdateClientBrandManualPayload,
  PagedClientBrandManuals,
} from "@/lib/api/types";

const base = (clientId: string) =>
  `/api/social/clients/${clientId}/brand-manuals`;

export async function listClientBrandManuals(
  clientId: string,
  params: { includeInactive?: boolean; page?: number; pageSize?: number } = {},
  token?: string,
): Promise<PagedClientBrandManuals> {
  const qs = new URLSearchParams();
  if (params.includeInactive) qs.set("includeInactive", "true");
  if (params.page !== undefined) qs.set("page", String(params.page));
  if (params.pageSize !== undefined) qs.set("pageSize", String(params.pageSize));
  const query = qs.toString();
  return apiRequest<PagedClientBrandManuals>(
    `${base(clientId)}${query ? `?${query}` : ""}`,
    undefined,
    token,
  );
}

export async function getClientBrandManualById(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientBrandManual> {
  return apiRequest<ClientBrandManual>(`${base(clientId)}/${id}`, undefined, token);
}

export async function getActiveClientBrandManual(
  clientId: string,
  token?: string,
): Promise<ClientBrandManual> {
  return apiRequest<ClientBrandManual>(`${base(clientId)}/active`, undefined, token);
}

export async function createClientBrandManual(
  clientId: string,
  payload: CreateClientBrandManualPayload,
  token?: string,
): Promise<ClientBrandManual> {
  return apiRequest<ClientBrandManual>(
    base(clientId),
    { method: "POST", body: JSON.stringify(payload) },
    token,
  );
}

export async function updateClientBrandManual(
  clientId: string,
  id: string,
  payload: UpdateClientBrandManualPayload,
  token?: string,
): Promise<ClientBrandManual> {
  return apiRequest<ClientBrandManual>(
    `${base(clientId)}/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}

export async function deactivateClientBrandManual(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientBrandManual> {
  return apiRequest<ClientBrandManual>(
    `${base(clientId)}/${id}/deactivate`,
    { method: "POST" },
    token,
  );
}

export async function activateClientBrandManual(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientBrandManual> {
  return apiRequest<ClientBrandManual>(
    `${base(clientId)}/${id}/activate`,
    { method: "POST" },
    token,
  );
}
