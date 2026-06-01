using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.GeneralStrategies;

public static class GeneralStrategyErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.general_strategies.not_found",
        "social.general_strategies.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.general_strategies.validation", detail);

    public static readonly Error EmptyStrategy = Error.Validation(
        "social.general_strategies.empty_strategy",
        "social.general_strategies.errors.empty_strategy");

    public static readonly Error AlreadyExists = Error.Conflict(
        "social.general_strategies.already_exists",
        "social.general_strategies.errors.already_exists");
}
