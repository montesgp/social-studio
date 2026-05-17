using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Tenants;

public static class TenantErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "tenants.not_found",
        "tenants.errors.not_found");

    public static readonly Error NameRequired = Error.Validation(
        "tenants.name_required",
        "tenants.errors.name_required");

    public static readonly Error NameTooLong = Error.Validation(
        "tenants.name_too_long",
        "tenants.errors.name_too_long");

    public static readonly Error OwnerRequired = Error.Validation(
        "tenants.owner_required",
        "tenants.errors.owner_required");

    public static readonly Error TenantHeaderMissing = Error.Failure(
        "tenants.header_missing",
        "tenants.errors.header_missing");

    public static readonly Error MembershipRequired = Error.Failure(
        "tenants.membership_required",
        "tenants.errors.membership_required");

    public static readonly Error AlreadyInactive = Error.Conflict(
        "tenants.already_inactive",
        "tenants.errors.already_inactive");

    public static readonly Error AlreadyActive = Error.Conflict(
        "tenants.already_active",
        "tenants.errors.already_active");
}
