using Incoders.Hive.Domain.Social.ClientStrategies;

namespace Incoders.Hive.Application.Features.Social.ClientStrategies.GetByClientId;

public sealed record ClientStrategyResponse(
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
    public static ClientStrategyResponse FromDomain(ClientStrategy strategy) =>
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
