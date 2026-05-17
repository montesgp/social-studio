namespace Incoders.Hive.Domain.Social.Clients;

public readonly record struct ClientId(Guid Value)
{
    public static ClientId New() => new(Guid.NewGuid());
}
