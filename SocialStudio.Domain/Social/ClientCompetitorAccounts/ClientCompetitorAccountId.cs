namespace SocialStudio.Domain.Social.ClientCompetitorAccounts;

public readonly record struct ClientCompetitorAccountId(Guid Value)
{
    public static ClientCompetitorAccountId New() => new(Guid.NewGuid());
}
