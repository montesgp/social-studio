using Incoders.Hive.Domain.Common;
using Incoders.Hive.Application.Features.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Activate;

public sealed record ActivateSocialClientCommand(
    Guid Id,
    Guid OrganizationId) : IRequest<Result<SocialClientResponse>>;
