using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientMonthlyObjectives;

public static class ClientMonthlyObjectiveErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.client_monthly_objectives.not_found",
        "social.client_monthly_objectives.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.client_monthly_objectives.validation", detail);

    public static readonly Error InvalidYear = Error.Validation(
        "social.client_monthly_objectives.invalid_year",
        "social.client_monthly_objectives.errors.invalid_year");

    public static readonly Error InvalidMonth = Error.Validation(
        "social.client_monthly_objectives.invalid_month",
        "social.client_monthly_objectives.errors.invalid_month");

    public static readonly Error BriefRequired = Error.Validation(
        "social.client_monthly_objectives.brief_required",
        "social.client_monthly_objectives.errors.brief_required");

    public static readonly Error AlreadyExists = Error.Conflict(
        "social.client_monthly_objectives.already_exists",
        "social.client_monthly_objectives.errors.already_exists");
}
