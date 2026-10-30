using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Activate;

public sealed record ActivateClientCompetitorAccountCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientCompetitorAccountResponse>>;
