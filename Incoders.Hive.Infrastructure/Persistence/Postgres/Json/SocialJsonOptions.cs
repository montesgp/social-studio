using System.Text.Json;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Json;

/// <summary>
/// Canonical System.Text.Json options for serializing social-domain jsonb collections.
/// Shared across all social aggregate jsonb properties (brand manuals, strategies, objectives).
/// SSOT — never recreate per-call.
/// </summary>
internal static class SocialJsonOptions
{
    /// <summary>
    /// Static singleton options instance. Pinned to CamelCase + no-indentation so the
    /// on-disk JSON shape is stable across deployments.
    /// </summary>
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
