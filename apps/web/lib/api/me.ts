import { apiRequest } from "@/lib/api/client";

export interface MeResponse {
  userId: string | null;
  email: string | null;
  isPlatformAdmin: boolean;
}

export async function getCurrentUser(token: string): Promise<MeResponse> {
  return apiRequest<MeResponse>("/api/me", undefined, token);
}
