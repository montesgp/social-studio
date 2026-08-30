using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Update;

public sealed record UpdateClientOwnAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id,
    Guid? SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes) : IRequest<Result<ClientOwnAccountResponse>>;
