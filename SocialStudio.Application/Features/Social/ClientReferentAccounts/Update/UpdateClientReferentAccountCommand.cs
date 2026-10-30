using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.Update;

public sealed record UpdateClientReferentAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id,
    Guid? SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes) : IRequest<Result<ClientReferentAccountResponse>>;
