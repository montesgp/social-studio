using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.GetById;

public sealed record GetClientOwnAccountByIdQuery(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id) : IRequest<Result<ClientOwnAccountResponse>>;
