using SocialStudio.Domain.Common;
using SocialStudio.Application.Features.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.Deactivate;

public sealed record DeactivateSocialClientCommand(
    Guid Id,
    Guid OrganizationId) : IRequest<Result<SocialClientResponse>>;
