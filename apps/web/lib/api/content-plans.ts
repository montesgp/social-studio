import { apiRequest } from "@/lib/api/client";
import type { ContentPlan, ContentPlanSummary, UpsertContentPlanPayload } from "@/lib/api/types";

const base = (clientId: string) => `/api/social/clients/${clientId}/content-plans`;

function is404(err: unknown): boolean {
  return (err as { problem?: { status?: number } })?.problem?.status === 404;
}

export async function getContentPlan(
  clientId: string,
  year: number,
  month: number,
  token?: string,
): Promise<ContentPlan | null> {
  try {
    return await apiRequest<ContentPlan>(
      `${base(clientId)}/${year}/${month}`,
      undefined,
      token,
    );
  } catch (err) {
    if (is404(err)) return null;
    throw err;
  }
}

export async function listContentPlans(
  clientId: string,
  token?: string,
): Promise<ContentPlanSummary[]> {
  return apiRequest<ContentPlanSummary[]>(base(clientId), undefined, token);
}

export async function upsertContentPlan(
  clientId: string,
  year: number,
  month: number,
  payload: UpsertContentPlanPayload,
  token?: string,
): Promise<ContentPlan> {
  return apiRequest<ContentPlan>(
    `${base(clientId)}/${year}/${month}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}
