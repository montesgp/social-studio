using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.Activate;

public sealed record ActivateSocialNetworkCommand(Guid Id) : IRequest<Result<SocialNetworkResponse>>;
