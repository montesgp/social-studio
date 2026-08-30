using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Niches.GetById;

public sealed record GetNicheByIdQuery(Guid Id) : IRequest<Result<NicheResponse>>;
