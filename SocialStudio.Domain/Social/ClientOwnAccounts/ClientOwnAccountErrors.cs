using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.ClientOwnAccounts;

public static class ClientOwnAccountErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.client_own_accounts.not_found",
        "social.client_own_accounts.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.client_own_accounts.validation", detail);

    public static Error ProfileUrlAlreadyExists(string profileUrl) =>
        Error.Conflict(
            "social.client_own_accounts.profile_url_already_exists",
            "social.client_own_accounts.errors.profile_url_already_exists");

    public static readonly Error AlreadyActive = Error.Conflict(
        "social.client_own_accounts.already_active",
        "social.client_own_accounts.errors.already_active");

    public static readonly Error AlreadyInactive = Error.Conflict(
        "social.client_own_accounts.already_inactive",
        "social.client_own_accounts.errors.already_inactive");

    public static readonly Error SocialNetworkInactive = Error.Validation(
        "social.client_own_accounts.social_network_inactive",
        "social.client_own_accounts.errors.social_network_inactive");

    public static Error InvalidProfileUrl(string detail) =>
        Error.Validation("social.client_own_accounts.invalid_profile_url", detail);

    public static Error InvalidHandle(string detail) =>
        Error.Validation("social.client_own_accounts.invalid_handle", detail);

    public static Error InvalidLabel(string detail) =>
        Error.Validation("social.client_own_accounts.invalid_label", detail);

    public static Error InvalidNotes(string detail) =>
        Error.Validation("social.client_own_accounts.invalid_notes", detail);
}
