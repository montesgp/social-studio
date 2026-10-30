namespace SocialStudio.Domain.Social.ClientReferentAccounts;

public readonly record struct ClientReferentAccountId(Guid Value)
{
    public static ClientReferentAccountId New() => new(Guid.NewGuid());
}
