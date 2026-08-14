using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Niches.Get;

public sealed record GetClientNichesQuery(
    Guid ClientId,
    Guid? OrganizationId) : IRequest<Result<ClientNichesResponse>>;
