using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientCompetitorAccounts.Sync;

public sealed record SyncClientCompetitorAccountsCommand(
    Guid OrganizationId,
    Guid ClientId,
    IReadOnlyList<SyncClientCompetitorAccountItem> Items)
    : IRequest<Result<IReadOnlyList<ClientCompetitorAccountResponse>>>;

public sealed record SyncClientCompetitorAccountItem(
    Guid? Id,
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes,
    bool IsActive);
