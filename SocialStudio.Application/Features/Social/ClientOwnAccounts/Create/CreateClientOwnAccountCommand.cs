using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.Create;

public sealed record CreateClientOwnAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes) : IRequest<Result<ClientOwnAccountResponse>>;
