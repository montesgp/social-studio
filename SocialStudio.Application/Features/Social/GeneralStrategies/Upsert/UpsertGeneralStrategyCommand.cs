using SocialStudio.Application.Features.Social.GeneralStrategies.GetByClientId;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.GeneralStrategies.Upsert;

public sealed record UpsertGeneralStrategyCommand(
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<string>? ContentPillars,
    string? TargetAudience,
    string? PostingFrequency,
    string? Notes) : IRequest<Result<GeneralStrategyResponse>>;
