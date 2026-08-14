using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Niches.Update;

// Code is deliberately absent: it is the stable key downstream data points at, mirroring the
// social network catalog.
public sealed record UpdateNicheCommand(
    Guid Id,
    string DisplayName,
    string? IconKey,
    IReadOnlyList<string> SearchHashtags) : IRequest<Result<NicheResponse>>;
