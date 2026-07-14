import { apiRequest } from "@/lib/api/client";
import type {
  ClientAestheticProfileAdmin,
  UpdateClientAestheticProfileBackgroundPatternPayload,
  UpdateClientAestheticProfileBackgroundPatternResult,
} from "@/lib/api/types";

const base = (clientId: string) => `/api/social/clients/${clientId}/aesthetic-profile`;

export async function getClientAestheticProfile(
  clientId: string,
  token?: string,
): Promise<ClientAestheticProfileAdmin> {
  return apiRequest<ClientAestheticProfileAdmin>(base(clientId), undefined, token);
}

export async function updateClientAestheticProfileBackgroundPattern(
  clientId: string,
  payload: UpdateClientAestheticProfileBackgroundPatternPayload,
  token?: string,
): Promise<UpdateClientAestheticProfileBackgroundPatternResult> {
  return apiRequest<UpdateClientAestheticProfileBackgroundPatternResult>(
    `${base(clientId)}/background-pattern`,
    { method: "PUT", body: JSON.stringify(payload) },
    token,
  );
}
