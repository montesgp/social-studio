using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.GetById;

public sealed record GetNicheByIdQuery(Guid Id) : IRequest<Result<NicheResponse>>;
