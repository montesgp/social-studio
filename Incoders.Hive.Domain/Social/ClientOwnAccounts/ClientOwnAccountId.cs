namespace Incoders.Hive.Domain.Social.ClientOwnAccounts;

public readonly record struct ClientOwnAccountId(Guid Value)
{
    public static ClientOwnAccountId New() => new(Guid.NewGuid());
}
