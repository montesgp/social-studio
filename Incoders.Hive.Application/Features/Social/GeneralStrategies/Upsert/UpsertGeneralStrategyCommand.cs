using Incoders.Hive.Application.Features.Social.GeneralStrategies.GetByClientId;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.GeneralStrategies.Upsert;

public sealed record UpsertGeneralStrategyCommand(
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<string>? ContentPillars,
    string? TargetAudience,
    string? PostingFrequency,
    string? Notes) : IRequest<Result<GeneralStrategyResponse>>;
