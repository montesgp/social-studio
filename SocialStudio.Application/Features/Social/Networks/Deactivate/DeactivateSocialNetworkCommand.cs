using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.Deactivate;

public sealed record DeactivateSocialNetworkCommand(Guid Id) : IRequest<Result<SocialNetworkResponse>>;
