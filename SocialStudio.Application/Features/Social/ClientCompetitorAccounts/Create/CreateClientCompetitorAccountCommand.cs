using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Create;

public sealed record CreateClientCompetitorAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes) : IRequest<Result<ClientCompetitorAccountResponse>>;
