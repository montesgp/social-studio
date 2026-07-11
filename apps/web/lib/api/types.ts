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
  /** Primary category (first declared) — back-compat for single-category consumers. */
  category: IntegrationProviderCategory;
  /** All categories this connector serves; the same credential is listed under each section. */
  categories: IntegrationProviderCategory[];
  description: string | null;
  iconKey: string;
  supportsBringYourOwnKey: boolean;
  supportsManagedByIncoders: boolean;
  supportsSystemDefault: boolean;
  systemDefaultReadOnly: boolean;
  credentialFields: IntegrationCredentialField[];
  /** Models for the primary category — back-compat. Prefer modelsByCategory for routing. */
  availableModels: string[];
  /** Catalog model menus keyed by category name (e.g. "AiModel", "MediaGeneration"). */
  modelsByCategory: Record<string, string[]>;
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
  selectedAiModel: string | null;
  lastTestedAtUtc: string | null;
  lastError: string | null;
  supportsBringYourOwnKey: boolean;
  supportsManagedByIncoders: boolean;
  supportsSystemDefault: boolean;
  systemDefaultReadOnly: boolean;
  availableModels: string[];
  // True only on the provider's primary-category card when the backend has a model catalog client
  // for it (e.g. OpenAI→AI models, ComfyUI→Media generation). Drives the "Sync models" affordance.
  supportsModelSync: boolean;
  credentialFields: IntegrationCredentialField[];
  configuredCredentialKeys: string[];
  credentialValues: Record<string, string>;
}

export interface OrganizationAiModel {
  id: string;
  organizationId: string;
  providerKey: string;
  modelId: string;
  displayName: string;
  isAvailable: boolean;
  syncedAtUtc: string;
}

export interface OrganizationAiModelsResponse {
  organizationId: string;
  providerKey: string;
  lastSyncedAtUtc: string | null;
  models: OrganizationAiModel[];
}

export interface WorkflowStepConfiguration {
  id: string;
  organizationId: string;
  workflowKey: string;
  analysisType: string;
  stepKey: string;
  operationKey: string | null;
  networkKey: string | null;
  providerKey: string;
  organizationIntegrationId: string;
  modelId: string | null;
  actorId: string | null;
  priority: number;
  isEnabled: boolean;
  updatedAtUtc: string;
}

export interface WorkflowStepCatalog {
  key: string;
  label: string;
  operationKey: string;
  operationLabel: string;
  description: string;
  requiresNetwork: boolean;
  supportedNetworkKeys: string[];
  allowedProviderCategories: IntegrationProviderCategory[];
  allowedProviderKeys: string[];
  supportsModelSelection: boolean;
  supportsActorSelection: boolean;
  actorVariantKey: string | null;
}

export interface WorkflowAnalysisTypeCatalog {
  key: string;
  label: string;
  steps: WorkflowStepCatalog[];
}

export interface WorkflowCatalog {
  key: string;
  label: string;
  analysisTypes: WorkflowAnalysisTypeCatalog[];
}

export interface WorkflowConfigurationCatalog {
  workflows: WorkflowCatalog[];
}

export interface WorkflowConfigurationOverview {
  organizationId: string;
  workflowKey: string | null;
  catalog: WorkflowConfigurationCatalog;
  steps: WorkflowStepConfiguration[];
}

export interface WorkflowStepConfigurationInput {
  analysisType: string;
  stepKey: string;
  operationKey?: string | null;
  networkKey?: string | null;
  providerKey: string;
  modelId?: string | null;
  actorId?: string | null;
  priority: number;
  isEnabled: boolean;
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
  checklist: IntegrationSetupChecklistItem[];
  checklistGroups: IntegrationSetupChecklist[];
  integrations: OrganizationIntegration[];
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
  socialNetworkId?: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
}

export interface SyncClientReferentAccountPayload {
  id?: string;
  socialNetworkId: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
  isActive: boolean;
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

export interface ClientCompetitorAccount {
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

export interface CreateClientCompetitorAccountPayload {
  socialNetworkId: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
}

export interface UpdateClientCompetitorAccountPayload {
  socialNetworkId?: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
}

export interface SyncClientCompetitorAccountPayload {
  id?: string;
  socialNetworkId: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
  isActive: boolean;
}

export interface ListClientCompetitorAccountsQuery {
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
}

export interface PagedClientCompetitorAccounts {
  items: ClientCompetitorAccount[];
  totalCount: number;
}

export interface ClientOwnAccount {
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

export interface CreateClientOwnAccountPayload {
  socialNetworkId: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
}

export interface UpdateClientOwnAccountPayload {
  socialNetworkId?: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
}

export interface SyncClientOwnAccountPayload {
  id?: string;
  socialNetworkId: string;
  profileUrl: string;
  handle?: string;
  label?: string;
  notes?: string;
  isActive: boolean;
}

export interface ListClientOwnAccountsQuery {
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
}

export interface PagedClientOwnAccounts {
  items: ClientOwnAccount[];
  totalCount: number;
}

export type BackgroundSourcingPolicy = "ClientDefinedSet" | "AiPalette" | "ClientAesthetic" | null;

export interface ColorEntry {
  name: string;
  hex: string;
  role: string | null;
  rank?: number;
}

export interface SocialClient {
  id: string;
  organizationId: string;
  organizationName?: string | null;
  name: string;
  description: string | null;
  sectorOrNiche?: string | null;
  backgroundSourcingPolicy?: BackgroundSourcingPolicy;
  /** Per-client aesthetic refresh staleness threshold in days. Null = system default (30 days). */
  aestheticRefreshThresholdDays?: number | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
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
  targetAudience: string | null;
  postingFrequency: string | null;
  notes: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface UpsertClientStrategyPayload {
  contentPillars: string[];
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

// Status string for the latest content-generation job linked to an analysis.
// Mirrors the backend ContentGenerationJob.Status values (free-form strings rather than enum on
// the wire, to allow forward-compatible additions without breaking the contract).
export type ContentGenerationJobStatusLabel =
  | "Pending"
  | "Running"
  // issue #67 (BAPA-STATUS-01): non-terminal phase for the first-time ClientAesthetic blocking
  // wait — parked between Running and generation resuming/failing.
  | "AnalyzingProfile"
  | "Completed"
  | "Partial"
  | "Failed"
  | string;

// Summary embedded in each saved-analysis row so the grid can render the right action.
// `hasPackage` true → the package ZIP can be re-downloaded without rerunning the pipeline.
export interface ContentGenerationSummary {
  jobId: string;
  status: ContentGenerationJobStatusLabel;
  hasPackage: boolean;
  packageJobId: string | null;
  completedAt: string | null;
}

export interface SingleContentAnalysisHistoryItem {
  id: string;
  clientId: string | null;
  sourceUrl: string;
  normalizedUrl: string | null;
  sourcePlatformKey: string | null;
  status: SingleContentAnalysisStatus;
  failureCode: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  completedAtUtc: string | null;
  latestContentGeneration: ContentGenerationSummary | null;
}

export interface SingleContentAnalysisHistoryResponse {
  items: SingleContentAnalysisHistoryItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface FindSingleContentAnalysisByUrlResponse {
  item: SingleContentAnalysisHistoryItem | null;
}

// Async/poll status view: snapshot/analysis are null until those stages complete.
// Returned by POST /analyze (202 Accepted, status=Requested) and by GET /analyze/{id} (polling).
export interface SingleAnalyzerStatusResponse {
  id: string;
  organizationId: string;
  clientId: string | null;
  sourceUrl: string;
  normalizedUrl: string | null;
  platformKey: string;
  status: SingleContentAnalysisStatus;
  snapshot: SingleAnalyzerContentSnapshot | null;
  analysis: SingleAnalyzerAnalysis | null;
  failureCode: string | null;
  failureMessageKey: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  completedAtUtc: string | null;
  aiAnalysisFailedAttemptCount?: number;
  isAiAnalysisRetryBlocked?: boolean;
  aiAnalysisRetryBlockedAtUtc?: string | null;
}

export interface Provenance {
  source: string;
  status: string;
  generatedAtUtc: string | null;
  promptVersion: string | null;
}

export interface ContentPlanCalendarSlot {
  id: string;
  day: number;
  contentFormat: string;
  sortOrder: number;
  producedPackageId: string | null;
  provenance: Provenance;
}

export interface ContentPlan {
  id: string;
  clientId: string;
  organizationId: string;
  periodKind: string;
  year: number;
  month: number;
  objectiveText: string;
  kpis: string[];
  objectiveProvenance: Provenance;
  contentStrategyText: string | null;
  contentStrategyProvenance: Provenance;
  slots: ContentPlanCalendarSlot[];
  calendarProvenance: Provenance;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ContentPlanSummary {
  id: string;
  clientId: string;
  periodKind: string;
  year: number;
  month: number;
  objectiveProvenance: Provenance;
  updatedAtUtc: string;
}

export interface UpsertContentPlanPayload {
  objectives: string;
  kpis?: string[];
  contentStrategyText?: string | null;
  slots?: { day: number; contentFormat: string }[];
}

// ── Content Generation types (single-analyzer package pipeline) ──────────────

export type ContentFormatKey =
  | "story-single"
  | "story-carousel"
  | "feed-single"
  | "feed-carousel"
  | "reel-script"
  | "content-idea"
  | "cta-variation"
  | "caption"
  | "hook";

export type PackageManifestFormatKey = ContentFormatKey | "story" | "feed-post" | "carousel" | "script" | "editable-package";

export interface GeneratePackageRequest {
  clientId?: string | null;
  requestedFormats: ContentFormatKey[];
  regenerationObjection?: string | null;
}

export interface GeneratePackageResponse {
  jobId: string;
  statusUrl: string;
}

/** Status values returned by the backend for the overall job. */
export type ContentGenerationJobStatus =
  | "Queued"
  | "Running"
  // issue #67 (BAPA-STATUS-01): first-time ClientAesthetic generation with no usable aesthetic
  // blocks on the enqueued/in-flight ProfileAnalysisJob; the job reports this status while
  // waiting. Non-terminal — the poller keeps polling through it (see TERMINAL_STATUSES in
  // use-content-generation-job.ts).
  | "AnalyzingProfile"
  | "Completed"
  | "Partial"
  | "PartialSuccess"
  | "Failed";

/** Manifest metadata for a single rendered format asset. */
export interface FormatPublicationMetadata {
  description: string;
}

/** File-level manifest metadata for assembled package entries. */
export interface PackageManifestFileMetadata {
  filename: string;
  relative_path: string;
  mime: string;
  size_bytes: number;
  content_hash: string;
}

/** Manifest metadata for a single rendered format asset. */
export interface FormatAssetMetadata {
  formatKey?: PackageManifestFormatKey;
  format_key?: PackageManifestFormatKey;
  mime: string;
  filename: string;
  relative_path?: string;
  sizeBytes?: number;
  size_bytes?: number;
  contentHash?: string;
  content_hash?: string;
  publication?: FormatPublicationMetadata | null;
  files?: PackageManifestFileMetadata[];
  assets?: PackageManifestFileMetadata[];
}

/** Client-first manifest section used by assembled downloadable packages. */
export interface GeneratedPackageClientManifest {
  client_folder_name: string;
  formats?: Partial<Record<PackageManifestFormatKey, FormatAssetMetadata>>;
  inferred_fields?: string[];
  warnings?: string[];
  total_size_bytes?: number;
  has_stored_assets?: boolean;
}

/**
 * Deserialized from ManifestJson on completed/partial jobs. Every field here is optional because
 * the background worker currently persists a reduced manifest (overallStatus + per-format status
 * only) and the full manifest with inferred fields / warnings / size lives on the orchestrator
 * result that did NOT make it to the persisted JSON. Consumers must defensive-default these.
 */
export interface GeneratedPackageManifest {
  clients?: Record<string, GeneratedPackageClientManifest>;
  /** Legacy flat manifest shape kept for already-persisted jobs. */
  formats?: Partial<Record<PackageManifestFormatKey, FormatAssetMetadata>>;
  inferredFields?: string[];
  inferred_fields?: string[];
  warnings?: string[];
  totalSizeBytes?: number;
  total_size_bytes?: number;
  /** Normalized client subfolder used inside the ZIP (e.g. "acme-corp"). Present in packages
   *  generated after the per-client folder feature; absent in legacy flat-layout packages.
   *  NOTE: the backend serializes with snake_case so this key is `client_folder_name` in JSON. */
  client_folder_name?: string;
  /**
   * Set to `true` when the job ran through the INC-3+ durable per-asset storage pipeline.
   * When true, use the assemble endpoint; when false/absent, use the legacy frozen-ZIP download.
   * NOTE: serialized as `has_stored_assets` (snake_case) by the backend.
   */
  has_stored_assets?: boolean;
}

export interface PackageActionState {
  downloadCount: number;
  lastDownloadedAtUtc: string | null;
  canvaSendCount: number;
  lastSentToCanvaAtUtc: string | null;
}

/**
 * Response from GET /api/social/content-generation-jobs/{jobId}/status.
 * perFormatStatus is a flat dict: formatKey → status string (e.g. "Rendered", "Failed").
 * manifest is populated (parsed from ManifestJson) when status is Completed or Partial.
 */
export interface ContentGenerationJobStatusResponse {
  jobId: string;
  status: ContentGenerationJobStatus;
  perFormatStatus: Partial<Record<ContentFormatKey, string>>;
  /** Failed attempts per format across jobs for this analysis/client scope. */
  perFormatFailureCounts: Partial<Record<ContentFormatKey, number>>;
  manifest: GeneratedPackageManifest | null;
  /** Failure reason from the backend (codes embedded), used to show actionable error guidance. */
  statusReason: string | null;
  /** Persisted package actions, used to show previous downloads/sends and discourage abuse. */
  packageActionState: PackageActionState | null;
}

// ── INC-6: Multi-client package tree types ────────────────────────────────────

/** One stored asset (e.g. a carousel slide or a single-asset format file). */
export interface PackageAssetEntry {
  index: number;
  filename: string;
  storagePath: string;
  sizeBytes: number;
}

/** One format's accumulated assets for a client. */
export interface PackageFormatEntry {
  formatKey: ContentFormatKey;
  filename: string;
  mime: string;
  assets: PackageAssetEntry[];
}

/** One client's accumulated formats for this analysis. */
export interface ClientPackageEntry {
  clientId: string;
  formats: PackageFormatEntry[];
}

/** Response from GET /api/social/single-analyzer/{analysisId}/packages. */
export interface AnalysisPackagesResponse {
  clients: ClientPackageEntry[];
}
