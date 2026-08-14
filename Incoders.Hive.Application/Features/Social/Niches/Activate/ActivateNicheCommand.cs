using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Niches.Activate;

public sealed record ActivateNicheCommand(Guid Id) : IRequest<Result<NicheResponse>>;
