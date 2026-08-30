import { apiRequest } from "@/lib/api/client";
import { EmailSmtpConfiguration, EmailTemplate, PlatformIntegrationSummary } from "@/lib/api/types";

export interface UpsertEmailTemplatePayload {
  subject: string;
  htmlBody: string;
  textBody?: string | null;
}

export interface UpsertEmailSmtpConfigurationPayload {
  enabled: boolean;
  host: string;
  port: number;
  useSsl: boolean;
  username: string;
  password?: string | null;
  fromEmail: string;
  fromName: string;
}

export async function listEmailTemplates(token?: string) {
  return apiRequest<EmailTemplate[]>("/api/platform/email-templates", undefined, token);
}

export async function upsertEmailTemplate(key: string, payload: UpsertEmailTemplatePayload, token?: string) {
  return apiRequest<EmailTemplate>(
    `/api/platform/email-templates/${encodeURIComponent(key)}`,
    { method: "PUT", body: JSON.stringify(payload) },
    token
  );
}

export async function listPlatformIntegrations(token?: string) {
  return apiRequest<PlatformIntegrationSummary[]>("/api/platform/integrations", undefined, token);
}

export async function getEmailSmtpConfiguration(token?: string) {
  return apiRequest<EmailSmtpConfiguration>("/api/platform/integrations/email-smtp", undefined, token);
}

export async function upsertEmailSmtpConfiguration(payload: UpsertEmailSmtpConfigurationPayload, token?: string) {
  return apiRequest<EmailSmtpConfiguration>(
    "/api/platform/integrations/email-smtp",
    { method: "PUT", body: JSON.stringify(payload) },
    token
  );
}
