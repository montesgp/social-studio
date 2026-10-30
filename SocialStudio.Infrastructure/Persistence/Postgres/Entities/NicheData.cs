namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for platform.niches.
/// No annotations — all configuration is done via fluent API in AppIdentityDbContext.
/// </summary>
public sealed class NicheData
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? IconKey { get; set; }

    /// <summary>Curated discovery hashtags, stored as a jsonb array to keep ordering stable.</summary>
    public List<string> SearchHashtags { get; set; } = new();

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
