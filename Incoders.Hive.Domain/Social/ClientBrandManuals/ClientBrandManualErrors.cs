using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

public static class ClientBrandManualErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.client_brand_manuals.not_found",
        "social.client_brand_manuals.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.client_brand_manuals.validation", detail);

    public static readonly Error EmptyManual = Error.Validation(
        "social.client_brand_manuals.empty_manual",
        "social.client_brand_manuals.errors.empty_manual");

    public static readonly Error AlreadyActive = Error.Conflict(
        "social.client_brand_manuals.already_active",
        "social.client_brand_manuals.errors.already_active");

    public static readonly Error AlreadyInactive = Error.Conflict(
        "social.client_brand_manuals.already_inactive",
        "social.client_brand_manuals.errors.already_inactive");

    public static readonly Error AnotherManualActive = Error.Conflict(
        "social.client_brand_manuals.another_active",
        "social.client_brand_manuals.errors.another_manual_active");
}
