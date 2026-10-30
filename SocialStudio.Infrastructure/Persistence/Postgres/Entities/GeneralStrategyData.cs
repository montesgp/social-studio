namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for social.general_strategies.
/// No annotations — all configuration done via fluent API in AppIdentityDbContext.
/// No navigation properties — sibling aggregate pattern.
/// Renamed from ClientStrategyData; identical column structure.
/// </summary>
public sealed class GeneralStrategyData
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public Guid OrganizationId { get; set; }

    public List<string> ContentPillars { get; set; } = new();

    public string? TargetAudience { get; set; }

    public string? PostingFrequency { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
