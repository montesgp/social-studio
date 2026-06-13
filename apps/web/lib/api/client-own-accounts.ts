import { apiRequest } from "@/lib/api/client";
import type {
  ClientOwnAccount,
  CreateClientOwnAccountPayload,
  UpdateClientOwnAccountPayload,
  ListClientOwnAccountsQuery,
  PagedClientOwnAccounts,
  SyncClientOwnAccountPayload,
} from "@/lib/api/types";

const base = (clientId: string) => `/api/social/clients/${clientId}/own-accounts`;

export async function listClientOwnAccounts(
  clientId: string,
  query: ListClientOwnAccountsQuery = {},
  token?: string,
  organizationId?: string,
): Promise<PagedClientOwnAccounts> {
  const params = new URLSearchParams();
  if (query.includeInactive) params.set("includeInactive", "true");
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiRequest<PagedClientOwnAccounts>(
    `${base(clientId)}${qs ? `?${qs}` : ""}`,
    undefined,
    token,
    organizationId,
  );
}

export async function getClientOwnAccountById(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientOwnAccount> {
  return apiRequest<ClientOwnAccount>(`${base(clientId)}/${id}`, undefined, token);
}

export async function createClientOwnAccount(
  clientId: string,
  payload: CreateClientOwnAccountPayload,
  token?: string,
): Promise<ClientOwnAccount> {
  return apiRequest<ClientOwnAccount>(
    base(clientId),
    { method: "POST", body: JSON.stringify(payload) },
    token,
  );
}

export async function updateClientOwnAccount(
  clientId: string,
  id: string,
  payload: UpdateClientOwnAccountPayload,
  token?: string,
): Promise<ClientOwnAccount> {
  return apiRequest<ClientOwnAccount>(
    `${base(clientId)}/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}

export async function deactivateClientOwnAccount(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientOwnAccount> {
  return apiRequest<ClientOwnAccount>(
    `${base(clientId)}/${id}/deactivate`,
    { method: "POST" },
    token,
  );
}

export async function activateClientOwnAccount(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientOwnAccount> {
  return apiRequest<ClientOwnAccount>(
    `${base(clientId)}/${id}/activate`,
    { method: "POST" },
    token,
  );
}

export async function reorderClientOwnAccounts(
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

export async function syncClientOwnAccounts(
  clientId: string,
  items: SyncClientOwnAccountPayload[],
  token?: string,
  organizationId?: string,
): Promise<ClientOwnAccount[]> {
  return apiRequest<ClientOwnAccount[]>(
    base(clientId),
    { method: "PUT", body: JSON.stringify({ items }) },
    token,
    organizationId,
  );
}
