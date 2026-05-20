using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.Create;

public sealed record CreateSocialClientCommand(
    Guid OrganizationId,
    string Name,
    string? Description,
    string? SectorOrNiche = null) : IRequest<Result<SocialClientResponse>>;
