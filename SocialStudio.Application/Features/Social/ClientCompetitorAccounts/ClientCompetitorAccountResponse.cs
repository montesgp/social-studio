using SocialStudio.Domain.Social.ClientCompetitorAccounts;
using SocialStudio.Domain.Social.Networks;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts;

public sealed record ClientCompetitorAccountResponse(
    Guid Id,
    Guid ClientId,
    Guid OrganizationId,
    Guid SocialNetworkId,
    string SocialNetworkCode,
    string SocialNetworkDisplayName,
    string SocialNetworkIconKey,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static ClientCompetitorAccountResponse FromDomain(
        ClientCompetitorAccount account,
        SocialNetwork network) => new(
            account.Id.Value,
            account.ClientId.Value,
            account.OrganizationId,
            account.SocialNetworkId.Value,
            network.Code,
            network.DisplayName,
            network.IconKey,
            account.ProfileUrl,
            account.Handle,
            account.Label,
            account.Notes,
            account.IsActive,
            account.SortOrder,
            account.CreatedAtUtc,
            account.UpdatedAtUtc);
}

public sealed record PagedClientCompetitorAccountsResponse(
    IReadOnlyList<ClientCompetitorAccountResponse> Items,
    int TotalCount);
