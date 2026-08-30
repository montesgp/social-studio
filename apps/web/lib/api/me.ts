import { apiRequest } from "@/lib/api/client";

export interface MeResponse {
  userId: string | null;
  email: string | null;
  isPlatformAdmin: boolean;
}

export interface WorkspaceModuleResponse {
  id: string;
  key: string;
  label: string;
  description: string;
  shortLabel: string;
}

export async function getCurrentUser(token: string): Promise<MeResponse> {
  return apiRequest<MeResponse>("/api/me", undefined, token);
}

export async function getWorkspaceModules(token: string): Promise<WorkspaceModuleResponse[]> {
  return apiRequest<WorkspaceModuleResponse[]>("/api/me/workspace-modules", undefined, token);
}
