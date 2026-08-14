using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Niches.Create;

public sealed record CreateNicheCommand(
    string Code,
    string DisplayName,
    string? IconKey,
    IReadOnlyList<string> SearchHashtags) : IRequest<Result<NicheResponse>>;
