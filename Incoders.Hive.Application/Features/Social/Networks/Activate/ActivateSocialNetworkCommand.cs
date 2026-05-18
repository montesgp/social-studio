using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Networks.Activate;

public sealed record ActivateSocialNetworkCommand(Guid Id) : IRequest<Result<SocialNetworkResponse>>;
