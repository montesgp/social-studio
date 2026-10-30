using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Update;

public sealed record UpdateClientCompetitorAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id,
    Guid? SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes) : IRequest<Result<ClientCompetitorAccountResponse>>;
