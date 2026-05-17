export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [key: string]: unknown;
}

export interface ApiError {
  message: string;
  problem?: ProblemDetails;
}

export interface PageResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface Organization {
  id: string;
  name: string;
  ownerUserId: string;
  isActive: boolean;
  deactivatedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface SocialClient {
  id: string;
  organizationId: string;
  name: string;
  description: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export type ThemeKey = "dark" | "light" | "system";

export interface UserPreference {
  id: string | null;
  userId: string;
  themeKey: ThemeKey;
  accentTheme: string | null;
  createdAtUtc: string | null;
  updatedAtUtc: string | null;
}

export type IntegrationProviderCategory =
  | "AiModel"
  | "Design"
  | "DataExtraction"
  | "Publishing"
  | "MediaGeneration";

export type IntegrationConnectionMode = "BringYourOwnKey" | "ManagedByIncoders" | "SystemDefault";

export type IntegrationStatus = "NotConfigured" | "Connected" | "Failed" | "Disabled";

export interface IntegrationCredentialField {
  key: string;
  label: string;
  isSecret: boolean;
  isRequired: boolean;
  placeholder: string | null;
  helpText: string | null;
}

export interface IntegrationProvider {
  id: string;
  key: string;
  displayName: string;
  category: IntegrationProviderCategory;
  description: string | null;
  iconKey: string;
  supportsBringYourOwnKey: boolean;
  supportsManagedByIncoders: boolean;
  supportsSystemDefault: boolean;
  systemDefaultReadOnly: boolean;
  credentialFields: IntegrationCredentialField[];
}

export interface OrganizationIntegration {
  integrationId: string | null;
  organizationId: string;
  providerKey: string;
  providerDisplayName: string;
  category: IntegrationProviderCategory;
  iconKey: string;
  connectionMode: IntegrationConnectionMode;
  status: IntegrationStatus;
  isReadOnly: boolean;
  isDefaultAi: boolean;
  selectedAiModel: string | null;
  lastTestedAtUtc: string | null;
  lastError: string | null;
  supportsBringYourOwnKey: boolean;
  supportsManagedByIncoders: boolean;
  supportsSystemDefault: boolean;
  systemDefaultReadOnly: boolean;
  credentialFields: IntegrationCredentialField[];
  configuredCredentialKeys: string[];
}

export interface IntegrationSetupChecklistItem {
  key: string;
  label: string;
  isRequired: boolean;
  isCompleted: boolean;
  requirement: string;
}

export interface OrganizationIntegrationsOverview {
  organizationId: string;
  defaultAiProviderKey: string | null;
  defaultAiModel: string | null;
  checklist: IntegrationSetupChecklistItem[];
  integrations: OrganizationIntegration[];
}
