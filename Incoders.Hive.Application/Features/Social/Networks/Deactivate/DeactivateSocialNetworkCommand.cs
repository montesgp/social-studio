using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Networks.Deactivate;

public sealed record DeactivateSocialNetworkCommand(Guid Id) : IRequest<Result<SocialNetworkResponse>>;
