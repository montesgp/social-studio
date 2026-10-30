using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.Activate;

public sealed record ActivateNicheCommand(Guid Id) : IRequest<Result<NicheResponse>>;
