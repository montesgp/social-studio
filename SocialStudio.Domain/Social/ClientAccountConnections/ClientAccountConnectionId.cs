namespace SocialStudio.Domain.Social.ClientAccountConnections;

public readonly record struct ClientAccountConnectionId(Guid Value)
{
    public static ClientAccountConnectionId New() => new(Guid.NewGuid());
}
