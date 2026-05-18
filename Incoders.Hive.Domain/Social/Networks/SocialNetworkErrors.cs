using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Networks;

public static class SocialNetworkErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.networks.not_found",
        "social.networks.errors.not_found");

    public static Error CodeAlreadyExists(string code) =>
        Error.Conflict(
            "social.networks.code_already_exists",
            $"social.networks.errors.code_already_exists");

    public static readonly Error AlreadyActive = Error.Conflict(
        "social.networks.already_active",
        "social.networks.errors.already_active");

    public static readonly Error AlreadyInactive = Error.Conflict(
        "social.networks.already_inactive",
        "social.networks.errors.already_inactive");

    public static Error InvalidCode(string detail) =>
        Error.Validation("social.networks.invalid_code", detail);

    public static Error InvalidDisplayName(string detail) =>
        Error.Validation("social.networks.invalid_display_name", detail);

    public static Error InvalidIconKey(string detail) =>
        Error.Validation("social.networks.invalid_icon_key", detail);
}
