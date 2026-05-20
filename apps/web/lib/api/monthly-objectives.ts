import { apiRequest } from "@/lib/api/client";
import type { MonthlyObjective, UpsertMonthlyObjectivePayload } from "@/lib/api/types";

const base = (clientId: string) => `/api/social/clients/${clientId}/objectives`;

function is404(err: unknown): boolean {
  return (err as { problem?: { status?: number } })?.problem?.status === 404;
}

export async function getMonthlyObjective(
  clientId: string,
  year: number,
  month: number,
  token?: string,
): Promise<MonthlyObjective | null> {
  try {
    return await apiRequest<MonthlyObjective>(
      `${base(clientId)}/${year}/${month}`,
      undefined,
      token,
    );
  } catch (err) {
    if (is404(err)) return null;
    throw err;
  }
}

export async function listMonthlyObjectives(
  clientId: string,
  token?: string,
): Promise<MonthlyObjective[]> {
  const result = await apiRequest<MonthlyObjective[]>(base(clientId), undefined, token);
  return result;
}

export async function upsertMonthlyObjective(
  clientId: string,
  year: number,
  month: number,
  payload: UpsertMonthlyObjectivePayload,
  token?: string,
): Promise<MonthlyObjective> {
  return apiRequest<MonthlyObjective>(
    `${base(clientId)}/${year}/${month}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}
