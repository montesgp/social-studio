using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ProfileAnalysis;

/// <summary>
/// Domain error constants for <see cref="ProfileAnalysis"/> invariant violations.
/// </summary>
public static class ProfileAnalysisErrors
{
    public static readonly Error InvalidStatusTransition = Error.Validation(
        "social.profile_analysis.invalid_status_transition",
        "social.profile_analysis.errors.invalid_status_transition");

    public static readonly Error NoUsableOwnerPosts = Error.Failure(
        "social.profile_analysis.no_usable_owner_posts",
        "social.profile_analysis.errors.no_usable_owner_posts");

    public static readonly Error OrganizationRequired = Error.Validation(
        "social.profile_analysis.organization_required",
        "social.profile_analysis.errors.organization_required");

    public static readonly Error ProfileSourceRequired = Error.Validation(
        "social.profile_analysis.profile_source_required",
        "social.profile_analysis.errors.profile_source_required");

    public static readonly Error NotFound = Error.NotFound(
        "social.profile_analysis.not_found",
        "social.profile_analysis.errors.not_found");

    public static readonly Error NotAnalyzed = Error.Validation(
        "social.profile_analysis.not_analyzed",
        "social.profile_analysis.errors.not_analyzed");

    public static readonly Error ClientRequired = Error.Validation(
        "social.profile_analysis.client_required",
        "social.profile_analysis.errors.client_required");

    public static readonly Error ProfileSourceMutuallyExclusive = Error.Validation(
        "social.profile_analysis.profile_source_mutually_exclusive",
        "social.profile_analysis.errors.profile_source_mutually_exclusive");

    public static readonly Error OwnAccountNotFound = Error.Validation(
        "social.profile_analysis.own_account_not_found",
        "social.profile_analysis.errors.own_account_not_found");

    /// <summary>
    /// The analysis cannot be retried with AI-only because the scrape data was not persisted
    /// (the scrape itself failed). A full re-run is required.
    /// Maps to 422 Unprocessable Entity at the API layer.
    /// </summary>
    public static readonly Error NotResumable = Error.Validation(
        "social.profile_analysis.not_resumable",
        "social.profile_analysis.errors.not_resumable");
}
