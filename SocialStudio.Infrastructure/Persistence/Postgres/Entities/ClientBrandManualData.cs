using SocialStudio.Domain.Social.ClientBrandManuals;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for social.client_brand_manuals.
/// No annotations — all configuration done via fluent API in AppIdentityDbContext.
/// No navigation properties — sibling aggregate pattern.
/// Collection properties typed as Domain records — Infrastructure is allowed to reference Domain.
/// </summary>
public sealed class ClientBrandManualData
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public Guid OrganizationId { get; set; }

    public string Title { get; set; } = string.Empty;

    public List<ColorEntry> ColorPalette { get; set; } = new();

    public List<LogoEntry> Logos { get; set; } = new();

    public List<BackgroundEntry> BackgroundTemplates { get; set; } = new();

    public List<TypographyEntry> Typography { get; set; } = new();

    public string? VoiceAndTone { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
