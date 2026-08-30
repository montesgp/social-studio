import { apiRequest } from "@/lib/api/client";
import type {
  ClientCompetitorAccount,
  CreateClientCompetitorAccountPayload,
  UpdateClientCompetitorAccountPayload,
  ListClientCompetitorAccountsQuery,
  PagedClientCompetitorAccounts,
  SyncClientCompetitorAccountPayload,
} from "@/lib/api/types";

const base = (clientId: string) => `/api/social/clients/${clientId}/competitor-accounts`;

export async function listClientCompetitorAccounts(
  clientId: string,
  query: ListClientCompetitorAccountsQuery = {},
  token?: string,
  organizationId?: string,
): Promise<PagedClientCompetitorAccounts> {
  const params = new URLSearchParams();
  if (query.includeInactive) params.set("includeInactive", "true");
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiRequest<PagedClientCompetitorAccounts>(
    `${base(clientId)}${qs ? `?${qs}` : ""}`,
    undefined,
    token,
    organizationId,
  );
}

export async function getClientCompetitorAccountById(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientCompetitorAccount> {
  return apiRequest<ClientCompetitorAccount>(`${base(clientId)}/${id}`, undefined, token);
}

export async function createClientCompetitorAccount(
  clientId: string,
  payload: CreateClientCompetitorAccountPayload,
  token?: string,
): Promise<ClientCompetitorAccount> {
  return apiRequest<ClientCompetitorAccount>(
    base(clientId),
    { method: "POST", body: JSON.stringify(payload) },
    token,
  );
}

export async function updateClientCompetitorAccount(
  clientId: string,
  id: string,
  payload: UpdateClientCompetitorAccountPayload,
  token?: string,
): Promise<ClientCompetitorAccount> {
  return apiRequest<ClientCompetitorAccount>(
    `${base(clientId)}/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}

export async function deactivateClientCompetitorAccount(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientCompetitorAccount> {
  return apiRequest<ClientCompetitorAccount>(
    `${base(clientId)}/${id}/deactivate`,
    { method: "POST" },
    token,
  );
}

export async function activateClientCompetitorAccount(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientCompetitorAccount> {
  return apiRequest<ClientCompetitorAccount>(
    `${base(clientId)}/${id}/activate`,
    { method: "POST" },
    token,
  );
}

export async function reorderClientCompetitorAccounts(
  clientId: string,
  orderedIds: string[],
  token?: string,
): Promise<void> {
  await apiRequest<void>(
    `${base(clientId)}/reorder`,
    { method: "POST", body: JSON.stringify({ orderedIds }) },
    token,
  );
}

export async function syncClientCompetitorAccounts(
  clientId: string,
  items: SyncClientCompetitorAccountPayload[],
  token?: string,
  organizationId?: string,
): Promise<ClientCompetitorAccount[]> {
  return apiRequest<ClientCompetitorAccount[]>(
    base(clientId),
    { method: "PUT", body: JSON.stringify({ items }) },
    token,
    organizationId,
  );
}
