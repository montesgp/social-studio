namespace Incoders.Hive.Domain.Social.NicheDiscovery;

public readonly record struct MarketId(Guid Value)
{
    public static MarketId New() => new(Guid.NewGuid());
}
