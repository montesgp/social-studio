using SocialStudio.Domain.Common;
using SocialStudio.Application.Features.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.Activate;

public sealed record ActivateSocialClientCommand(
    Guid Id,
    Guid OrganizationId) : IRequest<Result<SocialClientResponse>>;
