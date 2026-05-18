using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Networks.GetById;

public sealed record GetSocialNetworkByIdQuery(Guid Id) : IRequest<Result<SocialNetworkResponse>>;
