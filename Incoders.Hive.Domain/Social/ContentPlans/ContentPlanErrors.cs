using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ContentPlans;

public static class ContentPlanErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.content_plans.not_found",
        "social.content_plans.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.content_plans.validation", detail);

    public static readonly Error InvalidYear = Error.Validation(
        "social.content_plans.invalid_year",
        "social.content_plans.errors.invalid_year");

    public static readonly Error InvalidMonth = Error.Validation(
        "social.content_plans.invalid_month",
        "social.content_plans.errors.invalid_month");

    public static readonly Error InvalidDay = Error.Validation(
        "social.content_plans.invalid_day",
        "social.content_plans.errors.invalid_day");

    public static readonly Error ObjectiveRequired = Error.Validation(
        "social.content_plans.objective_required",
        "social.content_plans.errors.objective_required");

    public static readonly Error AlreadyExists = Error.Conflict(
        "social.content_plans.already_exists",
        "social.content_plans.errors.already_exists");

    public static readonly Error SlotNotFound = Error.NotFound(
        "social.content_plans.slot_not_found",
        "social.content_plans.errors.slot_not_found");
}
