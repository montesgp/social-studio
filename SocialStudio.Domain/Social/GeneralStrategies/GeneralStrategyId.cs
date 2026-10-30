namespace SocialStudio.Domain.Social.GeneralStrategies;

public readonly record struct GeneralStrategyId(Guid Value)
{
    public static GeneralStrategyId New() => new(Guid.NewGuid());
}
