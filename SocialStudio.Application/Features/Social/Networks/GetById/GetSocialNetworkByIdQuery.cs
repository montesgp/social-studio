using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.GetById;

public sealed record GetSocialNetworkByIdQuery(Guid Id) : IRequest<Result<SocialNetworkResponse>>;
