using Incoders.Hive.Application.Features.Social.ClientStrategies.GetByClientId;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientStrategies.Upsert;

public sealed record UpsertClientStrategyCommand(
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<string>? ContentPillars,
    IReadOnlyList<string>? ToneKeywords,
    string? TargetAudience,
    string? PostingFrequency,
    string? Notes) : IRequest<Result<ClientStrategyResponse>>;
