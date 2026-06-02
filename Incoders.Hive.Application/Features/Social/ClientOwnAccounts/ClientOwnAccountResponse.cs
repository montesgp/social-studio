using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Application.Features.Social.ClientOwnAccounts;

public sealed record ClientOwnAccountResponse(
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
    public static ClientOwnAccountResponse FromDomain(
        ClientOwnAccount account,
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

public sealed record PagedClientOwnAccountsResponse(
    IReadOnlyList<ClientOwnAccountResponse> Items,
    int TotalCount);
