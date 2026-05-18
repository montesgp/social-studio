using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Networks.Create;

public sealed record CreateSocialNetworkCommand(
    string Code,
    string DisplayName,
    string IconKey) : IRequest<Result<SocialNetworkResponse>>;
