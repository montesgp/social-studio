using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.Create;

public sealed record CreateSocialNetworkCommand(
    string Code,
    string DisplayName,
    string IconKey) : IRequest<Result<SocialNetworkResponse>>;
