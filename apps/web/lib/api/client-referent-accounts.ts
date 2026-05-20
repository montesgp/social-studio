import { apiRequest } from "@/lib/api/client";
import type {
  ClientReferentAccount,
  CreateClientReferentAccountPayload,
  UpdateClientReferentAccountPayload,
  ListClientReferentAccountsQuery,
  PagedClientReferentAccounts,
} from "@/lib/api/types";

const base = (clientId: string) => `/api/social/clients/${clientId}/referent-accounts`;

export async function listClientReferentAccounts(
  clientId: string,
  query: ListClientReferentAccountsQuery = {},
  token?: string,
): Promise<PagedClientReferentAccounts> {
  const params = new URLSearchParams();
  if (query.includeInactive) params.set("includeInactive", "true");
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiRequest<PagedClientReferentAccounts>(
    `${base(clientId)}${qs ? `?${qs}` : ""}`,
    undefined,
    token,
  );
}

export async function getClientReferentAccountById(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientReferentAccount> {
  return apiRequest<ClientReferentAccount>(`${base(clientId)}/${id}`, undefined, token);
}

export async function createClientReferentAccount(
  clientId: string,
  payload: CreateClientReferentAccountPayload,
  token?: string,
): Promise<ClientReferentAccount> {
  return apiRequest<ClientReferentAccount>(
    base(clientId),
    { method: "POST", body: JSON.stringify(payload) },
    token,
  );
}

export async function updateClientReferentAccount(
  clientId: string,
  id: string,
  payload: UpdateClientReferentAccountPayload,
  token?: string,
): Promise<ClientReferentAccount> {
  return apiRequest<ClientReferentAccount>(
    `${base(clientId)}/${id}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}

export async function deactivateClientReferentAccount(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientReferentAccount> {
  return apiRequest<ClientReferentAccount>(
    `${base(clientId)}/${id}/deactivate`,
    { method: "POST" },
    token,
  );
}

export async function activateClientReferentAccount(
  clientId: string,
  id: string,
  token?: string,
): Promise<ClientReferentAccount> {
  return apiRequest<ClientReferentAccount>(
    `${base(clientId)}/${id}/activate`,
    { method: "POST" },
    token,
  );
}

export async function reorderClientReferentAccounts(
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
