using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.MonthlyObjectives;

public static class MonthlyObjectiveErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.monthly_objectives.not_found",
        "social.monthly_objectives.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.monthly_objectives.validation", detail);

    public static readonly Error InvalidYear = Error.Validation(
        "social.monthly_objectives.invalid_year",
        "social.monthly_objectives.errors.invalid_year");

    public static readonly Error InvalidMonth = Error.Validation(
        "social.monthly_objectives.invalid_month",
        "social.monthly_objectives.errors.invalid_month");

    public static readonly Error BriefRequired = Error.Validation(
        "social.monthly_objectives.brief_required",
        "social.monthly_objectives.errors.brief_required");

    public static readonly Error AlreadyExists = Error.Conflict(
        "social.monthly_objectives.already_exists",
        "social.monthly_objectives.errors.already_exists");
}
