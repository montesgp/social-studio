import { apiRequest } from "@/lib/api/client";
import {
  IntegrationConnectionMode,
  IntegrationProvider,
  OrganizationRoutingPolicy,
  OrganizationIntegration,
  OrganizationIntegrationsOverview
} from "@/lib/api/types";

function organizationHeaders(organizationId: string) {
  return {
    "X-Tenant-Id": organizationId
  };
}

export interface IntegrationCredentialInput {
  key: string;
  value: string;
}

export interface ConfigureIntegrationPayload {
  connectionMode: IntegrationConnectionMode;
  enabled: boolean;
  selectedAiModel?: string;
  credentials?: IntegrationCredentialInput[];
}

export interface SetDefaultAiPayload {
  providerKey: string;
  model?: string;
}

export interface UpsertRoutingPolicyCandidatePayload {
  providerKey: string;
  priority: number;
  allowManagedFallback: boolean;
  maxAttempts: number;
  timeoutMs: number;
  selectedModel?: string;
  isEnabled: boolean;
}

export interface UpsertRoutingPolicyPayload {
  flowKey: string;
  capabilityKey: string;
  platformKey?: string;
  isEnabled: boolean;
  candidates: UpsertRoutingPolicyCandidatePayload[];
}

export async function listIntegrationProviders(token?: string) {
  return apiRequest<IntegrationProvider[]>("/api/integrations/providers", undefined, token);
}

export async function listOrganizationIntegrations(organizationId: string, token?: string) {
  return apiRequest<OrganizationIntegrationsOverview>(
    "/api/integrations/organization",
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function getOrganizationIntegration(organizationId: string, providerKey: string, token?: string) {
  return apiRequest<OrganizationIntegration>(
    `/api/integrations/organization/${providerKey}`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function configureOrganizationIntegration(
  organizationId: string,
  providerKey: string,
  payload: ConfigureIntegrationPayload,
  token?: string
) {
  return apiRequest<OrganizationIntegration>(
    `/api/integrations/organization/${providerKey}`,
    {
      method: "PUT",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify(payload)
    },
    token
  );
}

export async function deleteOrganizationIntegration(organizationId: string, providerKey: string, token?: string) {
  return apiRequest<void>(
    `/api/integrations/organization/${providerKey}`,
    {
      method: "DELETE",
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function testOrganizationIntegration(organizationId: string, providerKey: string, token?: string) {
  return apiRequest<OrganizationIntegration>(
    `/api/integrations/organization/${providerKey}/test`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function setDefaultAiIntegration(
  organizationId: string,
  payload: SetDefaultAiPayload,
  token?: string
) {
  return apiRequest<OrganizationIntegration>(
    "/api/integrations/organization/default-ai",
    {
      method: "PUT",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify(payload)
    },
    token
  );
}

export async function listOrganizationRoutingPolicies(
  organizationId: string,
  filters?: { flowKey?: string; capabilityKey?: string; platformKey?: string },
  token?: string
) {
  const params = new URLSearchParams();
  if (filters?.flowKey) params.set("flowKey", filters.flowKey);
  if (filters?.capabilityKey) params.set("capabilityKey", filters.capabilityKey);
  if (filters?.platformKey !== undefined) params.set("platformKey", filters.platformKey);

  const suffix = params.size > 0 ? `?${params.toString()}` : "";
  return apiRequest<OrganizationRoutingPolicy[]>(
    `/api/integrations/organization/routing-policies${suffix}`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function upsertOrganizationRoutingPolicy(
  organizationId: string,
  payload: UpsertRoutingPolicyPayload,
  token?: string
) {
  return apiRequest<OrganizationRoutingPolicy>(
    "/api/integrations/organization/routing-policies",
    {
      method: "PUT",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify(payload)
    },
    token
  );
}
