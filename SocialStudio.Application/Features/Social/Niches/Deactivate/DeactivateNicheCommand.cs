using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.Deactivate;

public sealed record DeactivateNicheCommand(Guid Id) : IRequest<Result<NicheResponse>>;
