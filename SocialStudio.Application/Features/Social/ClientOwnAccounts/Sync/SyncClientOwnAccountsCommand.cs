using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.Sync;

public sealed record SyncClientOwnAccountsCommand(
    Guid OrganizationId,
    Guid ClientId,
    IReadOnlyList<SyncClientOwnAccountItem> Items)
    : IRequest<Result<IReadOnlyList<ClientOwnAccountResponse>>>;

public sealed record SyncClientOwnAccountItem(
    Guid? Id,
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes,
    bool IsActive);
