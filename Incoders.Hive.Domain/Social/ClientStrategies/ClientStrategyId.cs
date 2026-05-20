namespace Incoders.Hive.Domain.Social.ClientStrategies;

public readonly record struct ClientStrategyId(Guid Value)
{
    public static ClientStrategyId New() => new(Guid.NewGuid());
}
