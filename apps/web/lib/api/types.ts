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
  organizationName?: string | null;
  name: string;
  description: string | null;
  sectorOrNiche?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export type ContentWorkflowSearchCriterion = "niche" | "references" | "viral" | "competitors" | "own";
export type ContentWorkflowStrategyMode = "mixed" | "educational" | "informative" | "entertainment";
export type ContentWorkflowNetworkSource = "tiktok" | "instagram" | "youtube";
export type ContentWorkflowActionType = "download_audio" | "transcribe_and_build_script" | "build_carousel";

export interface ContentWorkflowSession {
  workflowId: string;
  clientId: string;
  clientName: string;
  searchCriterion: ContentWorkflowSearchCriterion | string;
  strategyMode: ContentWorkflowStrategyMode | string;
  networkSources: string[];
  status: string;
  progressPercentage: number;
  progressMessage: string;
  totalResults: number;
  selectedCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ContentWorkflowStep {
  key: string;
  label: string;
  status: string;
  message: string;
  progressPercentage: number;
}

export interface ContentWorkflowStatus {
  workflowId: string;
  status: string;
  progressPercentage: number;
  progressMessage: string;
  steps: ContentWorkflowStep[];
  updatedAtUtc: string;
}

export interface ContentWorkflowResult {
  id: string;
  title: string;
  networkSource: string;
  searchCriterion: string;
  strategyMode: string;
  videoUrl: string;
  thumbnailUrl: string;
  summary: string;
  views: number;
  likes: number;
  comments: number;
  isSelected: boolean;
}

export interface ContentWorkflowPreview {
  workflowId: string;
  resultId: string;
  resultTitle: string;
  videoAnalysis: string;
  scriptDraft: string;
}

export interface ContentWorkflowSelection {
  workflowId: string;
  selectedCount: number;
  selectedResultIds: string[];
  updatedAtUtc: string;
}

export interface ContentWorkflowActionResult {
  actionId: string;
  workflowId: string;
  action: string;
  status: string;
  resultIds: string[];
  message: string;
  executedAtUtc: string;
}

export interface ContentWorkflowPackage {
  workflowId: string;
  packageFileName: string;
  downloadUrl: string;
  status: string;
  itemCount: number;
  generatedAtUtc: string;
}

export interface ContentWorkflowHistoryItem {
  workflowId: string;
  clientId: string;
  clientName: string;
  searchCriterion: string;
  strategyMode: string;
  networkSources: string[];
  status: string;
  totalResults: number;
  selectedCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ContentWorkflowHistory {
  summary: ContentWorkflowHistoryItem;
  actions: ContentWorkflowActionResult[];
}

export interface SocialNetwork {
  id: string;
  code: string;
  displayName: string;
  iconKey: string;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface EmailTemplate {
  key: string;
  displayName: string;
  description: string;
  subject: string;
  htmlBody: string;
  textBody: string | null;
  variables: string[];
  isConfigured: boolean;
  updatedAtUtc: string | null;
}

export interface PlatformIntegrationSummary {
  key: string;
  displayName: string;
  description: string;
  category: string;
  enabled: boolean;
  isConfigured: boolean;
  updatedAtUtc: string | null;
}

export interface EmailSmtpConfiguration {
  key: string;
  displayName: string;
  enabled: boolean;
  host: string;
  port: number;
  useSsl: boolean;
  username: string;
  fromEmail: string;
  fromName: string;
  passwordConfigured: boolean;
  updatedAtUtc: string | null;
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

export type IntegrationStatus = "NotConfigured" | "Configured" | "Connected" | "Failed" | "Disabled";

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
  availableModels: string[];
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
  credentialValues: Record<string, string>;
}

export interface IntegrationSetupChecklistItem {
  key: string;
  label: string;
  isRequired: boolean;
  isCompleted: boolean;
  requirement: string;
}

export interface IntegrationSetupChecklist {
  key: string;
  label: string;
  items: IntegrationSetupChecklistItem[];
}

export interface OrganizationIntegrationsOverview {
  organizationId: string;
  defaultAiProviderKey: string | null;
  defaultAiModel: string | null;
  checklist: IntegrationSetupChecklistItem[];
  checklistGroups: IntegrationSetupChecklist[];
  integrations: OrganizationIntegration[];
}

export interface OrganizationRoutingPolicyCandidate {
  providerId: string;
  providerKey: string;
  connectionMode: IntegrationConnectionMode;
  priority: number;
  allowManagedFallback: boolean;
  maxAttempts: number;
  timeoutMs: number;
  selectedModel: string | null;
  isEnabled: boolean;
}

export interface OrganizationRoutingPolicy {
  id: string;
  organizationId: string;
  flowKey: string;
  capabilityKey: string;
  platformKey: string | null;
  isEnabled: boolean;
  candidates: OrganizationRoutingPolicyCandidate[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ClientReferentAccount {
  id: string;
  clientId: string;
  organizationId: string;
  socialNetworkId: string;
  socialNetworkCode: string;
  socialNetworkDisplayName: string;
  socialNetworkIconKey: string;
  profileUrl: string;
  handle: string | null;
  label: string | null;
  notes: string | null;
  isActive: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateClientReferentAccountPayload {
  socialNetworkId: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
}

export interface UpdateClientReferentAccountPayload {
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
}

export interface ListClientReferentAccountsQuery {
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
}

export interface PagedClientReferentAccounts {
  items: ClientReferentAccount[];
  totalCount: number;
}

export interface ColorEntry {
  name: string;
  hex: string;
  role: string | null;
}

export interface LogoEntry {
  name: string;
  url: string;
  variant: string | null;
}

export interface BackgroundEntry {
  name: string;
  url: string;
  useCase: string | null;
}

export interface TypographyEntry {
  name: string;
  fontFamily: string;
  role: string | null;
  fontUrl: string | null;
  weight: string | null;
}

export interface ClientBrandManual {
  id: string;
  clientId: string;
  organizationId: string;
  title: string;
  colorPalette: ColorEntry[];
  logos: LogoEntry[];
  backgroundTemplates: BackgroundEntry[];
  typography: TypographyEntry[];
  voiceAndTone: string | null;
  notes: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateClientBrandManualPayload {
  title: string;
  colorPalette: ColorEntry[];
  logos: LogoEntry[];
  backgroundTemplates: BackgroundEntry[];
  typography: TypographyEntry[];
  voiceAndTone?: string;
  notes?: string;
}

export interface UpdateClientBrandManualPayload {
  title: string;
  colorPalette: ColorEntry[];
  logos: LogoEntry[];
  backgroundTemplates: BackgroundEntry[];
  typography: TypographyEntry[];
  voiceAndTone?: string;
  notes?: string;
}

export interface PagedClientBrandManuals {
  items: ClientBrandManual[];
  totalCount: number;
}

export interface ClientStrategy {
  id: string;
  clientId: string;
  organizationId: string;
  contentPillars: string[];
  toneKeywords: string[];
  targetAudience: string | null;
  postingFrequency: string | null;
  notes: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface UpsertClientStrategyPayload {
  contentPillars: string[];
  toneKeywords: string[];
  targetAudience?: string | null;
  postingFrequency?: string | null;
  notes?: string | null;
}

// ── Single Analyzer backend response types (mirrors SingleContentAnalysisResponse) ──

export type SingleContentAnalysisStatus =
  | "Requested"
  | "ResolvingContent"
  | "ContentResolved"
  | "ContentResolvedPartial"
  | "Analyzing"
  | "Analyzed"
  | "Failed";

export interface SingleAnalyzerMediaReference {
  kind: string;
  uri: string;
  mimeType: string | null;
}

export interface SingleAnalyzerContentSnapshot {
  platformKey: string;
  canonicalUrl: string;
  title: string | null;
  description: string | null;
  author: string | null;
  thumbnailUrl: string | null;
  durationSeconds: number | null;
  metrics: Record<string, number>;
  transcript: string | null;
  mediaReferences: SingleAnalyzerMediaReference[];
  rawPayloadReference: string | null;
  tags: string[];
  contentType: string | null;
  publishedAtUtc: string | null;
}

export interface SingleAnalyzerScore {
  novelty: number;
  clientFit: number;
  platformFit: number;
  hookQuality: number;
  feasibility: number;
  expectedEngagement: number;
}

export interface SingleAnalyzerIdea {
  title: string;
  concept: string;
  rationale: string;
  score: SingleAnalyzerScore;
}

export interface SingleAnalyzerAnalysis {
  summary: string;
  detectedHooks: string[];
  likelyAudience: string;
  narrativeStructure: string;
  strengths: string[];
  weaknesses: string[];
  derivedIdeas: SingleAnalyzerIdea[];
  clientAdaptationRecommendations: string[];
  confidence: number;
  warnings: string[];
}

export interface SingleAnalyzerWarning {
  code: string;
  messageKey: string;
}

export interface ProviderFailureDiagnostic {
  providerKey: string;
  errorCode: string;
  message: string;
}

export interface SingleContentAnalysisResponse {
  id: string;
  organizationId: string;
  clientId: string | null;
  userId: string;
  sourceUrl: string;
  normalizedUrl: string;
  platformKey: string;
  canonicalUrl: string;
  status: SingleContentAnalysisStatus;
  snapshot: SingleAnalyzerContentSnapshot;
  analysis: SingleAnalyzerAnalysis;
  warnings: SingleAnalyzerWarning[];
  diagnostics: ProviderFailureDiagnostic[];
  aiProviderKey: string;
  aiModel: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  completedAtUtc: string | null;
}

export interface MonthlyObjective {
  id: string;
  clientId: string;
  organizationId: string;
  year: number;
  month: number;
  objectives: string;
  kpis: string[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface UpsertMonthlyObjectivePayload {
  objectives: string;
  kpis?: string[];
}
