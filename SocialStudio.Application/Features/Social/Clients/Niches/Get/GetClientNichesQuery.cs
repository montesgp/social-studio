using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.Niches.Get;

public sealed record GetClientNichesQuery(
    Guid ClientId,
    Guid? OrganizationId) : IRequest<Result<ClientNichesResponse>>;
