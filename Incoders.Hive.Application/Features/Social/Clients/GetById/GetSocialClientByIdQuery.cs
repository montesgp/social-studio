using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.GetById;

public sealed record GetSocialClientByIdQuery(
    Guid Id,
    Guid OrganizationId) : IRequest<Result<SocialClientResponse>>;
