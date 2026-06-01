using Incoders.Hive.Domain.Social.GeneralStrategies;

namespace Incoders.Hive.Application.Features.Social.GeneralStrategies.GetByClientId;

public sealed record GeneralStrategyResponse(
    Guid Id,
    Guid ClientId,
    Guid OrganizationId,
    IReadOnlyList<string> ContentPillars,
    IReadOnlyList<string> ToneKeywords,
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
            strategy.ToneKeywords,
            strategy.TargetAudience,
            strategy.PostingFrequency,
            strategy.Notes,
            strategy.CreatedAtUtc,
            strategy.UpdatedAtUtc);
}
