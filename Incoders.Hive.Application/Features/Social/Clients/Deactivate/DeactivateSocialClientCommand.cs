using Incoders.Hive.Domain.Common;
using Incoders.Hive.Application.Features.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Deactivate;

public sealed record DeactivateSocialClientCommand(
    Guid Id,
    Guid OrganizationId) : IRequest<Result<SocialClientResponse>>;
