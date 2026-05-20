using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientStrategies;

public static class ClientStrategyErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social.client_strategies.not_found",
        "social.client_strategies.errors.not_found");

    public static Error Validation(string detail) =>
        Error.Validation("social.client_strategies.validation", detail);

    public static readonly Error EmptyStrategy = Error.Validation(
        "social.client_strategies.empty_strategy",
        "social.client_strategies.errors.empty_strategy");

    public static readonly Error AlreadyExists = Error.Conflict(
        "social.client_strategies.already_exists",
        "social.client_strategies.errors.already_exists");
}
