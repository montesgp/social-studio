using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.Create;

public sealed record CreateClientReferentAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes) : IRequest<Result<ClientReferentAccountResponse>>;
