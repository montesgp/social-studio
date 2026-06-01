namespace Incoders.Hive.Domain.Social.ContentPlans;

public readonly record struct ContentPlanId(Guid Value)
{
    public static ContentPlanId New() => new(Guid.NewGuid());
}
