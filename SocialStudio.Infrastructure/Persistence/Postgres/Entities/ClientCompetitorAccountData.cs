namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for social.client_competitor_accounts.
/// No annotations — all configuration is done via fluent API in AppIdentityDbContext.
/// No navigation properties — sibling aggregate pattern.
/// </summary>
public sealed class ClientCompetitorAccountData
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid SocialNetworkId { get; set; }

    public string ProfileUrl { get; set; } = string.Empty;

    public string? Handle { get; set; }

    public string? Label { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
