import { apiRequest } from "@/lib/api/client";

export interface SystemModuleSummary {
  id: string;
  moduleKey: string;
  displayName: string;
  description: string;
  shortLabel: string;
  createdAtUtc: string;
}

export interface OrganizationModuleAssignment {
  moduleId: string;
  moduleKey: string;
  displayName: string;
  description: string;
  shortLabel: string;
  sortOrder: number;
}

export interface OrganizationModulesResponse {
  organizationId: string;
  availableModules: SystemModuleSummary[];
  assignedModules: OrganizationModuleAssignment[];
}

export async function getOrganizationModules(organizationId: string, token?: string) {
  return apiRequest<OrganizationModulesResponse>(`/api/organizations/${organizationId}/modules`, undefined, token);
}

export async function updateOrganizationModules(
  organizationId: string,
  orderedModuleKeys: string[],
  token?: string
) {
  return apiRequest<OrganizationModulesResponse>(
    `/api/organizations/${organizationId}/modules`,
    {
      method: "PUT",
      body: JSON.stringify({ orderedModuleKeys }),
    },
    token
  );
}
