using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.GetById;

public sealed record GetClientReferentAccountByIdQuery(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientReferentAccountResponse>>;
