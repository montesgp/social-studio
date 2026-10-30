using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.Clients;

public static class ClientErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.clients.not_found",
        "social.clients.errors.not_found");

    public static readonly Error OrganizationRequired = Error.Validation(
        "social.clients.organization_required",
        "social.clients.errors.organization_required");

    public static readonly Error NameRequired = Error.Validation(
        "social.clients.name_required",
        "social.clients.errors.name_required");

    public static readonly Error NameTooLong = Error.Validation(
        "social.clients.name_too_long",
        "social.clients.errors.name_too_long");

    public static readonly Error DescriptionTooLong = Error.Validation(
        "social.clients.description_too_long",
        "social.clients.errors.description_too_long");

    public static readonly Error AlreadyInactive = Error.Conflict(
        "social.clients.already_inactive",
        "social.clients.errors.already_inactive");

    public static readonly Error AlreadyActive = Error.Conflict(
        "social.clients.already_active",
        "social.clients.errors.already_active");

    public static readonly Error SectorOrNicheTooLong = Error.Validation(
        "social.clients.sector_or_niche_too_long",
        "social.clients.errors.sector_or_niche_too_long");

    public static readonly Error AestheticRefreshThresholdTooLow = Error.Validation(
        "social.clients.aesthetic_refresh_threshold_too_low",
        "social.clients.errors.aesthetic_refresh_threshold_too_low");

    public static readonly Error AestheticRefreshThresholdTooHigh = Error.Validation(
        "social.clients.aesthetic_refresh_threshold_too_high",
        "social.clients.errors.aesthetic_refresh_threshold_too_high");

    public static readonly Error MaxPostsToAnalyzeOutOfRange = Error.Validation(
        "social.clients.max_posts_to_analyze_out_of_range",
        "social.clients.errors.max_posts_to_analyze_out_of_range");

    public static readonly Error MaxPiecesPerGenerationOutOfRange = Error.Validation(
        "social.clients.max_pieces_per_generation_out_of_range",
        "social.clients.errors.max_pieces_per_generation_out_of_range");
}
