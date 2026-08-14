namespace Incoders.Hive.Domain.Social.Niches;

public readonly record struct NicheId(Guid Value)
{
    public static NicheId New() => new(Guid.NewGuid());
}
