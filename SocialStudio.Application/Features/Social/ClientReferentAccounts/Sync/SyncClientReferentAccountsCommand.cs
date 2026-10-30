using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.Sync;

public sealed record SyncClientReferentAccountsCommand(
    Guid OrganizationId,
    Guid ClientId,
    IReadOnlyList<SyncClientReferentAccountItem> Items)
    : IRequest<Result<IReadOnlyList<ClientReferentAccountResponse>>>;

public sealed record SyncClientReferentAccountItem(
    Guid? Id,
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes,
    bool IsActive);
