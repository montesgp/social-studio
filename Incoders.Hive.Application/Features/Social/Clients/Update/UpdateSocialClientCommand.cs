using Incoders.Hive.Domain.Common;
using Incoders.Hive.Application.Features.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Update;

public sealed record UpdateSocialClientCommand(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Description,
    string? SectorOrNiche = null) : IRequest<Result<SocialClientResponse>>;
