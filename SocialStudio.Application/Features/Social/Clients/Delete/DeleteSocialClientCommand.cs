using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.Delete;

public sealed record DeleteSocialClientCommand(Guid Id) : IRequest<Result<DeletedSocialClientResponse>>;

public sealed record DeletedSocialClientResponse(
    Guid Id,
    Guid OrganizationId,
    string Name);
