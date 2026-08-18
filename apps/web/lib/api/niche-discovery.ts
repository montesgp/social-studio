// Client for the niche-discovery viability dashboard (design §10, §11).
//
// Thin on purpose: no decision logic lives here — sorting, filtering, availability formatting,
// score-pair rendering, evidence-link derivation, and narrative text all live under
// `lib/social/niche-discovery/*`, where they are unit-tested.

import { apiRequest } from "@/lib/api/client";

export interface MarketOption {
  code: string;
  displayNameKey: string;
  primaryLanguage: string;
}

export interface MarketCatalogResponse {
  markets: MarketOption[];
}

export interface StartDiscoveryRunRequest {
  sourceMarketCode: string;
  targetMarketCode: string;
  networkKey: string;
  contentFormat: string;
  windowDays?: number;
  depth?: string;
  nicheId?: string;
  seedTerms?: string[];
}

export interface StartDiscoveryRunResponse {
  runId: string;
  status: string;
}

export interface DiscoveryRunStatusResponse {
  runId: string;
  status: string;
  windowDays: number;
  depth: string;
  providerUnitsSpent: number;
  aiCompletionsSpent: number;
  semanticDegraded: boolean;
  unconfiguredSourceKeys: string[];
  failedSourceKeys: string[];
  failureReasonKey: string | null;
  targetMarketCode: string;
  contentFormat: string;
}

export interface ExcludedSignal {
  key: string;
  reasonKey: string;
  i18nKey: string;
}

export interface NicheOpportunitySummary {
  id: string;
  subNicheLabel: string | null;
  contentPattern: string;
  topicLabels: string[];
  opportunityScore: number;
  confidenceScore: number;
  effectiveWeightDenominator: number;
  excludedSignals: ExcludedSignal[];
  rank: number;
  targetMarketCompetition: number | null;
  automationSuitability: number | null;
  growthProxy: number | null;
}

export interface RunOpportunitiesResponse {
  opportunities: NicheOpportunitySummary[];
}

export interface NicheOpportunityRecord {
  id: string;
  runId: string;
  organizationId: string;
  nicheId: string | null;
  subNicheLabel: string | null;
  contentPattern: string;
  topicLabels: string[];
  opportunityScore: number;
  confidenceScore: number;
  effectiveWeightDenominator: number;
  excludedSignalsJson: string;
  medianViews: number | null;
  outlierRatio: number | null;
  outlierDensity: number | null;
  growthProxy: number | null;
  crossChannelRepetition: number | null;
  /** Run-relative rank of demand (0..1), NOT an absolute size. See `targetMarketRawDemand`. */
  targetMarketDemand: number | null;
  targetMarketCompetition: number | null;
  targetMarketGap: number | null;
  /** Demand before run-normalization — the only value carrying absolute magnitude. */
  targetMarketRawDemand: number | null;
  automationSuitability: number | null;
  channelSampleSize: number;
  videoSampleSize: number;
  rank: number;
  observedAtUtc: string;
}

export type NicheDiscoveryEvidenceKind = "Channel" | "Video";

export interface NicheDiscoveryEvidenceRecord {
  id: string;
  runId: string;
  opportunityId: string;
  organizationId: string;
  evidenceKind: NicheDiscoveryEvidenceKind;
  externalRef: string;
  channelHandle: string;
  title: string;
  thumbnailUrl: string | null;
  /** Provider-reported channel description. Review context only — no metric reads it. */
  description: string | null;
  publishedAtUtc: string | null;
  views: number | null;
  likes: number | null;
  comments: number | null;
  subscriberCount: number | null;
  totalViews: number | null;
  videoCount: number | null;
  durationSeconds: number | null;
  languageCode: string | null;
  /** Provider-reported ISO 3166 code of the owning channel. Review context only — no metric reads it. */
  country: string | null;
  sourceKey: string;
  observedAtUtc: string;
}

export interface OpportunityDetailResponse {
  opportunity: NicheOpportunityRecord;
  excludedSignals: ExcludedSignal[];
  evidence: NicheDiscoveryEvidenceRecord[];
}

export interface DiscoveryRunHistoryEntry {
  runId: string;
  status: string;
  sourceMarketCode: string;
  targetMarketCode: string;
  contentFormat: string;
  windowDays: number;
  depth: string;
  seedTerms: string[];
  semanticDegraded: boolean;
  failureReasonKey: string | null;
  providerUnitsSpent: number;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

export interface DiscoveryRunHistoryResponse {
  runs: DiscoveryRunHistoryEntry[];
}

export function listDiscoveryRuns(limit?: number, token?: string): Promise<DiscoveryRunHistoryResponse> {
  const query = limit ? `?limit=${limit}` : "";
  return apiRequest<DiscoveryRunHistoryResponse>(`/api/social/niche-discovery/runs${query}`, undefined, token);
}

export function listNicheDiscoveryMarkets(token?: string): Promise<MarketCatalogResponse> {
  return apiRequest<MarketCatalogResponse>("/api/social/niche-discovery/markets", undefined, token);
}

export function startDiscoveryRun(
  request: StartDiscoveryRunRequest,
  token?: string
): Promise<StartDiscoveryRunResponse> {
  return apiRequest<StartDiscoveryRunResponse>(
    "/api/social/niche-discovery/runs",
    { method: "POST", body: JSON.stringify(request) },
    token
  );
}

export function getDiscoveryRun(runId: string, token?: string): Promise<DiscoveryRunStatusResponse> {
  return apiRequest<DiscoveryRunStatusResponse>(`/api/social/niche-discovery/runs/${runId}`, undefined, token);
}

export function listRunOpportunities(runId: string, token?: string): Promise<RunOpportunitiesResponse> {
  return apiRequest<RunOpportunitiesResponse>(
    `/api/social/niche-discovery/runs/${runId}/opportunities`,
    undefined,
    token
  );
}

export function getOpportunityDetail(
  opportunityId: string,
  token?: string
): Promise<OpportunityDetailResponse> {
  return apiRequest<OpportunityDetailResponse>(
    `/api/social/niche-discovery/opportunities/${opportunityId}`,
    undefined,
    token
  );
}
