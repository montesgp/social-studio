import { apiRequest } from "@/lib/api/client";
import {
  ContentWorkflowActionResult,
  ContentWorkflowHistory,
  ContentWorkflowHistoryItem,
  ContentWorkflowPackage,
  ContentWorkflowPreview,
  ContentWorkflowResult,
  ContentWorkflowSearchCriterion,
  ContentWorkflowSelection,
  ContentWorkflowSession,
  ContentWorkflowStatus,
  ContentWorkflowStrategyMode,
  PageResult,
  SocialClient
} from "@/lib/api/types";

function organizationHeaders(organizationId: string) {
  return {
    "X-Tenant-Id": organizationId
  };
}

export interface StartContentWorkflowPayload {
  clientId: string;
  clientName: string;
  searchCriterion: ContentWorkflowSearchCriterion;
  strategyMode: ContentWorkflowStrategyMode;
  networkSources: string[];
}

export async function listWorkflowClients(organizationId: string, token?: string) {
  const params = new URLSearchParams({ page: "1", pageSize: "100", includeInactive: "false" });
  return apiRequest<PageResult<SocialClient>>(
    `/api/social/clients?${params.toString()}`,
    { headers: organizationHeaders(organizationId) },
    token
  );
}

export async function startContentWorkflow(organizationId: string, payload: StartContentWorkflowPayload, token?: string) {
  return apiRequest<ContentWorkflowSession>(
    "/api/social/workflows/content-generation",
    {
      method: "POST",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify(payload)
    },
    token
  );
}

export async function getContentWorkflowStatus(organizationId: string, workflowId: string, token?: string) {
  return apiRequest<ContentWorkflowStatus>(
    `/api/social/workflows/${workflowId}/status`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function listContentWorkflowResults(
  organizationId: string,
  workflowId: string,
  page: number,
  pageSize: number,
  token?: string
) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });

  return apiRequest<PageResult<ContentWorkflowResult>>(
    `/api/social/workflows/${workflowId}/results?${params.toString()}`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function getContentWorkflowPreview(
  organizationId: string,
  workflowId: string,
  resultId: string,
  token?: string
) {
  return apiRequest<ContentWorkflowPreview>(
    `/api/social/workflows/${workflowId}/results/${resultId}/preview`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function selectContentWorkflowResults(
  organizationId: string,
  workflowId: string,
  resultIds: string[],
  token?: string
) {
  return apiRequest<ContentWorkflowSelection>(
    `/api/social/workflows/${workflowId}/select`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify({ resultIds })
    },
    token
  );
}

export async function runContentWorkflowAction(
  organizationId: string,
  workflowId: string,
  resultId: string,
  action: string,
  token?: string
) {
  return apiRequest<ContentWorkflowActionResult>(
    `/api/social/workflows/${workflowId}/results/${resultId}/actions`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify({ action })
    },
    token
  );
}

export async function runContentWorkflowBulkAction(
  organizationId: string,
  workflowId: string,
  action: string,
  resultIds: string[],
  token?: string
) {
  return apiRequest<ContentWorkflowActionResult>(
    `/api/social/workflows/${workflowId}/actions/bulk`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId),
      body: JSON.stringify({ action, resultIds })
    },
    token
  );
}

export async function generateContentWorkflowPackage(organizationId: string, workflowId: string, token?: string) {
  return apiRequest<ContentWorkflowPackage>(
    `/api/social/workflows/${workflowId}/generate-package`,
    {
      method: "POST",
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function getContentWorkflowHistory(organizationId: string, workflowId: string, token?: string) {
  return apiRequest<ContentWorkflowHistory>(
    `/api/social/workflows/${workflowId}/history`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}

export async function listContentWorkflowHistory(organizationId: string, limit = 12, token?: string) {
  const params = new URLSearchParams({ limit: String(limit) });
  return apiRequest<ContentWorkflowHistoryItem[]>(
    `/api/social/workflows/history?${params.toString()}`,
    {
      headers: organizationHeaders(organizationId)
    },
    token
  );
}
