namespace Incoders.Hive.Domain.Social.MonthlyObjectives;

public readonly record struct MonthlyObjectiveId(Guid Value)
{
    public static MonthlyObjectiveId New() => new(Guid.NewGuid());
}
