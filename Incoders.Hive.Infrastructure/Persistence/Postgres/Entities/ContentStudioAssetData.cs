namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// jsonb item stored inside <c>social.content_studio_items.draft_assets</c>.
/// Mirrors the domain <c>ContentAssetRef</c> (soft URI references, never payloads).
/// </summary>
public sealed class ContentStudioAssetData
{
    public string Role { get; set; } = string.Empty;

    public string StorageUri { get; set; } = string.Empty;

    public string Mime { get; set; } = string.Empty;
}
