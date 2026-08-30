using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.SingleAnalyzer;

public static class SingleAnalyzerErrors
{
    public static readonly Error OrganizationRequired = Error.Validation(
        "social.single_analyzer.organization_required",
        "social.single_analyzer.errors.organization_required");

    public static readonly Error UserRequired = Error.Validation(
        "social.single_analyzer.user_required",
        "social.single_analyzer.errors.user_required");

    public static readonly Error UrlRequired = Error.Validation(
        "social.single_analyzer.url_required",
        "social.single_analyzer.errors.url_required");

    public static readonly Error UrlInvalid = Error.Validation(
        "social.single_analyzer.url_invalid",
        "social.single_analyzer.errors.url_invalid");

    public static readonly Error SnapshotInvalid = Error.Validation(
        "social.single_analyzer.snapshot_invalid",
        "social.single_analyzer.errors.snapshot_invalid");

    public static readonly Error AnalysisInvalid = Error.Validation(
        "social.single_analyzer.analysis_invalid",
        "social.single_analyzer.errors.analysis_invalid");

    public static readonly Error ScoreInvalid = Error.Validation(
        "social.single_analyzer.score_invalid",
        "social.single_analyzer.errors.score_invalid");

    // Pre-flight: a required provider API must be reachable BEFORE the workflow starts.
    public static readonly Error ExtractionProviderUnavailable = Error.Failure(
        "social.single_analyzer.extraction_provider_unavailable",
        "social.single_analyzer.errors.extraction_provider_unavailable");

    public static readonly Error AiProviderUnavailable = Error.Failure(
        "social.single_analyzer.ai_provider_unavailable",
        "social.single_analyzer.errors.ai_provider_unavailable");

    public static readonly Error NotFound = Error.NotFound(
        "social.single_analyzer.not_found",
        "social.single_analyzer.errors.not_found");

    public static readonly Error AnalysisFailed = Error.Failure(
        "social.single_analyzer.analysis_failed",
        "social.single_analyzer.errors.analysis_failed");

    public static readonly Error AiRetryBlocked = Error.Conflict(
        "social.single_analyzer.ai_retry_blocked",
        "social.single_analyzer.errors.ai_retry_blocked");
}
