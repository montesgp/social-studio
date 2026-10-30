namespace SocialStudio.Domain.Social.ClientBrandManuals;

public readonly record struct ClientBrandManualId(Guid Value)
{
    public static ClientBrandManualId New() => new(Guid.NewGuid());
}
