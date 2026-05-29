using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientCompetitorAccounts;

public static class ClientCompetitorAccountErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.client_competitor_accounts.not_found",
        "social.client_competitor_accounts.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.client_competitor_accounts.validation", detail);

    public static Error ProfileUrlAlreadyExists(string profileUrl) =>
        Error.Conflict(
            "social.client_competitor_accounts.profile_url_already_exists",
            "social.client_competitor_accounts.errors.profile_url_already_exists");

    public static readonly Error AlreadyActive = Error.Conflict(
        "social.client_competitor_accounts.already_active",
        "social.client_competitor_accounts.errors.already_active");

    public static readonly Error AlreadyInactive = Error.Conflict(
        "social.client_competitor_accounts.already_inactive",
        "social.client_competitor_accounts.errors.already_inactive");

    public static readonly Error SocialNetworkInactive = Error.Validation(
        "social.client_competitor_accounts.social_network_inactive",
        "social.client_competitor_accounts.errors.social_network_inactive");

    public static Error InvalidProfileUrl(string detail) =>
        Error.Validation("social.client_competitor_accounts.invalid_profile_url", detail);

    public static Error InvalidHandle(string detail) =>
        Error.Validation("social.client_competitor_accounts.invalid_handle", detail);

    public static Error InvalidLabel(string detail) =>
        Error.Validation("social.client_competitor_accounts.invalid_label", detail);

    public static Error InvalidNotes(string detail) =>
        Error.Validation("social.client_competitor_accounts.invalid_notes", detail);
}
