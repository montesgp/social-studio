import { apiRequest } from "@/lib/api/client";
import type { ClientStrategy, UpsertClientStrategyPayload } from "@/lib/api/types";

const base = (clientId: string) => `/api/social/clients/${clientId}/general-strategy`;

export async function getClientStrategy(
  clientId: string,
  token?: string,
): Promise<ClientStrategy | null> {
  try {
    return await apiRequest<ClientStrategy>(base(clientId), undefined, token);
  } catch (err) {
    const problem = (err as { problem?: { status?: number } })?.problem;
    if (problem?.status === 404) return null;
    throw err;
  }
}

export async function upsertClientStrategy(
  clientId: string,
  payload: UpsertClientStrategyPayload,
  token?: string,
): Promise<ClientStrategy> {
  return apiRequest<ClientStrategy>(
    base(clientId),
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}
