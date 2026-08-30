using Incoders.Hive.Domain.Social.GeneralStrategies;

namespace Incoders.Hive.Application.Features.Social.GeneralStrategies.GetByClientId;

public sealed record GeneralStrategyResponse(
    Guid Id,
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<string> ContentPillars,
    string? TargetAudience,
    string? PostingFrequency,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static GeneralStrategyResponse FromDomain(GeneralStrategy strategy) =>
        new(
            strategy.Id.Value,
            strategy.ClientId,
            strategy.OrganizationId,
            strategy.ContentPillars,
            strategy.TargetAudience,
            strategy.PostingFrequency,
            strategy.Notes,
            strategy.CreatedAtUtc,
            strategy.UpdatedAtUtc);
}
