namespace Incoders.Hive.Domain.Social.Networks;

public readonly record struct SocialNetworkId(Guid Value)
{
    public static SocialNetworkId New() => new(Guid.NewGuid());
}
