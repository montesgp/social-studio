import { apiRequest } from "@/lib/api/client";
import {
  IntegrationConnectionMode,
  IntegrationProvider,
  OrganizationActorsResponse,
  OrganizationAiModelsResponse,
  OrganizationIntegration,
  OrganizationIntegrationsOverview,
  WorkflowConfigurationOverview,
  WorkflowStepConfigurationInput
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

export interface CanvaAuthorizeUrlResponse {
  authorizeUrl: string;
}

/**
 * Starts the ORG-level Canva OAuth connect. Returns the Canva authorization URL the caller must
 * navigate the browser to. The `redirectUri` is the env-derived callback (see the Connect button);
 * it must be in the Canva app's allowed redirect URIs.
 */
export async function getCanvaAuthorizeUrl(
  organizationId: string,
  orgIntegrationId: string,
  redirectUri: string,
  token?: string
) {
  const query = new URLSearchParams({ orgIntegrationId, redirectUri }).toString();
  return apiRequest<CanvaAuthorizeUrlResponse>(
    `/api/integrations/canva/authorize?${query}`,
    { headers: organizationHeaders(organizationId) },
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

export async function listOrganizationAiModels(organizationId: string, providerKey: string, token?: string) {
  return apiRequest<OrganizationAiModelsResponse>(
    `/api/integrations/organization/${providerKey}/models`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function syncOrganizationAiModels(organizationId: string, providerKey: string, token?: string) {
  return apiRequest<OrganizationAiModelsResponse>(
    `/api/integrations/organization/${providerKey}/models/sync`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function listOrganizationActors(organizationId: string, providerKey: string, token?: string) {
  return apiRequest<OrganizationActorsResponse>(
    `/api/integrations/organization/${providerKey}/actors`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function syncOrganizationActors(organizationId: string, providerKey: string, token?: string) {
  return apiRequest<OrganizationActorsResponse>(
    `/api/integrations/organization/${providerKey}/actors/sync`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function listWorkflowConfigurations(
  organizationId: string,
  workflowKey?: string,
  token?: string
) {
  const query = new URLSearchParams({ organizationId });
  if (workflowKey) {
    query.set("workflowKey", workflowKey);
  }

  return apiRequest<WorkflowConfigurationOverview>(
    `/api/integrations/workflow-configurations?${query.toString()}`,
    undefined,
    token
  );
}

export async function upsertWorkflowConfigurations(
  organizationId: string,
  workflowKey: string,
  steps: WorkflowStepConfigurationInput[],
  token?: string
) {
  return apiRequest<WorkflowConfigurationOverview>(
    `/api/integrations/workflow-configurations/${organizationId}/${workflowKey}`,
    {
      method: "PUT",
      body: JSON.stringify({ steps })
    },
    token
  );
}
