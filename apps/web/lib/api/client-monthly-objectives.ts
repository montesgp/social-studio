import { apiRequest } from "@/lib/api/client";
import type { ClientMonthlyObjective, UpsertClientMonthlyObjectivePayload } from "@/lib/api/types";

const base = (clientId: string) => `/api/social/clients/${clientId}/objectives`;

function is404(err: unknown): boolean {
  return (err as { problem?: { status?: number } })?.problem?.status === 404;
}

export async function getClientMonthlyObjective(
  clientId: string,
  year: number,
  month: number,
  token?: string,
): Promise<ClientMonthlyObjective | null> {
  try {
    return await apiRequest<ClientMonthlyObjective>(
      `${base(clientId)}/${year}/${month}`,
      undefined,
      token,
    );
  } catch (err) {
    if (is404(err)) return null;
    throw err;
  }
}

export async function listClientMonthlyObjectives(
  clientId: string,
  token?: string,
): Promise<ClientMonthlyObjective[]> {
  const result = await apiRequest<ClientMonthlyObjective[]>(base(clientId), undefined, token);
  return result;
}

export async function upsertClientMonthlyObjective(
  clientId: string,
  year: number,
  month: number,
  payload: UpsertClientMonthlyObjectivePayload,
  token?: string,
): Promise<ClientMonthlyObjective> {
  return apiRequest<ClientMonthlyObjective>(
    `${base(clientId)}/${year}/${month}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}
