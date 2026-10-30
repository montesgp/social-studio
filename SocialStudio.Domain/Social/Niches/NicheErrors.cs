using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.Niches;

public static class NicheErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.niches.not_found",
        "social.niches.errors.not_found");

    /// <summary>
    /// A client may only associate niches that are currently active. Reactivate the catalog entry
    /// (or pick another one) instead of inventing a replacement inline.
    /// </summary>
    public static readonly Error Inactive = Error.Conflict(
        "social.niches.inactive",
        "social.niches.errors.inactive");

    public static Error CodeAlreadyExists(string code) =>
        Error.Conflict(
            "social.niches.code_already_exists",
            "social.niches.errors.code_already_exists");

    public static Error DisplayNameAlreadyExists(string displayName) =>
        Error.Conflict(
            "social.niches.display_name_already_exists",
            "social.niches.errors.display_name_already_exists");

    public static readonly Error AlreadyActive = Error.Conflict(
        "social.niches.already_active",
        "social.niches.errors.already_active");

    public static readonly Error AlreadyInactive = Error.Conflict(
        "social.niches.already_inactive",
        "social.niches.errors.already_inactive");

    public static Error InvalidCode(string detail) =>
        Error.Validation("social.niches.invalid_code", detail);

    public static Error InvalidDisplayName(string detail) =>
        Error.Validation("social.niches.invalid_display_name", detail);

    public static Error InvalidIconKey(string detail) =>
        Error.Validation("social.niches.invalid_icon_key", detail);

    public static Error InvalidHashtag(string detail) =>
        Error.Validation("social.niches.invalid_hashtag", detail);
}
