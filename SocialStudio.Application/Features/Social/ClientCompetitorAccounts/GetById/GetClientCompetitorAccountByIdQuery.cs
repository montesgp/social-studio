using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts.GetById;

public sealed record GetClientCompetitorAccountByIdQuery(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientCompetitorAccountResponse>>;
