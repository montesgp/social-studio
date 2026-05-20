using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Clients;

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
}
