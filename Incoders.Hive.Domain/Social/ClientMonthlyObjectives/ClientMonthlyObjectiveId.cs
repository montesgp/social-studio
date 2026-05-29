namespace Incoders.Hive.Domain.Social.ClientMonthlyObjectives;

public readonly record struct ClientMonthlyObjectiveId(Guid Value)
{
    public static ClientMonthlyObjectiveId New() => new(Guid.NewGuid());
}
