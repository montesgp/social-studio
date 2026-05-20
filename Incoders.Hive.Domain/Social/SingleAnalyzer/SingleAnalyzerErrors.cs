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
}
