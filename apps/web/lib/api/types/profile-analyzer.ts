// TypeScript types matching the backend ProfileAnalyzer DTOs.
// Source of truth: ProfileAnalysisContracts.cs + MapProfileAnalyzerEndpoints.cs
// All field names match the camelCase JSON output from .NET System.Text.Json (JsonSerializerDefaults.Web).

// ── Status lifecycle (PA-14) ──────────────────────────────────────────────────

export type ProfileAnalysisStatus =
  | "Requested"
  | "ScrapingProfile"
  | "ProfileScraped"
  | "ProfileScrapedPartial"
  | "Analyzing"
  | "Analyzed"
  | "Failed";

// ── Poll / start response ──────────────────────────────────────────────────────

/**
 * Returned by POST /analyze (202) and GET /analyze/{id} (polling).
 * snapshot/stats/aiAnalysis are null until those stages complete.
 */
export interface ProfileAnalysisStatusResponse {
  id: string;
  organizationId: string;
  clientId: string | null;
  ownAccountId: string | null;
  profileUrlOrHandle: string;
  status: ProfileAnalysisStatus;
  failureCode: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  completedAtUtc: string | null;
}

// ── Dashboard response (PA-16, PA-17, PA-18) ──────────────────────────────────

export interface ProfileSnapshotDto {
  instagramId: string;
  username: string;
  followersCount: number;
  postsCount: number;
  reelsCount: number | null;
  engagementRate: number;
  medianDaysBetweenPosts: number | null;
  profilePicUrl: string | null;
  biography: string | null;
  isVerified: boolean;
  isBusinessAccount: boolean;
  businessCategoryName: string | null;
}

export interface WeekdayMetricDto {
  avgLikes: number;
  avgComments: number;
  avgViews: number | null;
}

export interface ProfileStatsDto {
  engagementRate: number;
  medianViews: number | null;
  /** Keys are weekday integers 0-6 (Mon=0, Sun=6 — UTC). */
  avgLikesByWeekday: Record<string, WeekdayMetricDto>;
  avgCommentsByWeekday: Record<string, WeekdayMetricDto>;
  avgViewsByWeekday: Record<string, WeekdayMetricDto>;
  /** Keys are content type strings (e.g. "Video", "Image", "Carousel"); values are percentages 0-100. */
  contentTypeMix: Record<string, number>;
  postingCadenceDays: number;
  ownerPostCount: number;
  degradedPostCount: number;
}

export interface ProfilePostDto {
  postId: string;
  mediaType: string;
  likesCount: number;
  commentsCount: number;
  videoViewCount: number | null;
  timestamp: string;
  isPaidPartnership: boolean;
  hashtags: string[];
}

/** Comparison against the client's active ContentPlan/GeneralStrategy. Null for URL-path. */
export interface ProfileComparisonDto {
  contentPlanObjective: string | null;
  contentPlanKpis: string[];
  contentPlanStrategy: string | null;
  generalStrategyPillars: string[];
  generalStrategyAudience: string | null;
  generalStrategyPostingFrequency: string | null;
}

export interface ProfileDashboardResponse {
  id: string;
  organizationId: string;
  clientId: string | null;
  profileUrlOrHandle: string;
  status: ProfileAnalysisStatus;
  profile: ProfileSnapshotDto | null;
  stats: ProfileStatsDto | null;
  ownerPosts: ProfilePostDto[];
  /** Null for URL-path analyses or when no plan data exists (PA-17). */
  comparison: ProfileComparisonDto | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

// ── History list (PA-15) ───────────────────────────────────────────────────────

export interface ProfileAnalysisHistoryItemDto {
  id: string;
  clientId: string | null;
  profileUrlOrHandle: string;
  status: ProfileAnalysisStatus;
  failureCode: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

export interface ProfileAnalysisHistoryResponse {
  items: ProfileAnalysisHistoryItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ── Impute request (PA-20) ─────────────────────────────────────────────────────

/**
 * 11 actionable impute targets.
 * Logos and Slots are excluded (not AI-derivable — WU-01 spike findings).
 * Numeric values match the backend ProfileImputeTarget enum.
 */
export type ProfileImputeTarget =
  | "ColorPalette"
  | "Typography"
  | "BackgroundTemplates"
  | "VoiceAndTone"
  | "ObjectiveText"
  | "ObjectiveKpis"
  | "ContentStrategyText"
  | "ContentPillars"
  | "TargetAudience"
  | "PostingFrequency"
  | "ToneKeywords";

export interface ImputeProfileAnalysisRequest {
  targets: ProfileImputeTarget[];
}

// ── Own account picker item ────────────────────────────────────────────────────

/** Subset of ClientOwnAccount returned by /own-accounts?network=instagram. */
export interface OwnAccountItem {
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
