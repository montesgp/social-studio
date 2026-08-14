using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Niches.Deactivate;

public sealed record DeactivateNicheCommand(Guid Id) : IRequest<Result<NicheResponse>>;
