using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientReferentAccounts;

public static class ClientReferentAccountErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.client_referent_accounts.not_found",
        "social.client_referent_accounts.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.client_referent_accounts.validation", detail);

    public static Error ProfileUrlAlreadyExists(string profileUrl) =>
        Error.Conflict(
            "social.client_referent_accounts.profile_url_already_exists",
            "social.client_referent_accounts.errors.profile_url_already_exists");

    public static readonly Error AlreadyActive = Error.Conflict(
        "social.client_referent_accounts.already_active",
        "social.client_referent_accounts.errors.already_active");

    public static readonly Error AlreadyInactive = Error.Conflict(
        "social.client_referent_accounts.already_inactive",
        "social.client_referent_accounts.errors.already_inactive");

    public static readonly Error SocialNetworkInactive = Error.Validation(
        "social.client_referent_accounts.social_network_inactive",
        "social.client_referent_accounts.errors.social_network_inactive");

    public static Error InvalidProfileUrl(string detail) =>
        Error.Validation("social.client_referent_accounts.invalid_profile_url", detail);

    public static Error InvalidHandle(string detail) =>
        Error.Validation("social.client_referent_accounts.invalid_handle", detail);

    public static Error InvalidLabel(string detail) =>
        Error.Validation("social.client_referent_accounts.invalid_label", detail);

    public static Error InvalidNotes(string detail) =>
        Error.Validation("social.client_referent_accounts.invalid_notes", detail);
}
