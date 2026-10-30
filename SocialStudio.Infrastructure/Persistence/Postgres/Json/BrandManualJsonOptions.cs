using System.Text.Json;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Json;

/// <summary>
/// Canonical System.Text.Json options for serializing brand manual jsonb collections.
/// SSOT — never recreate per-call (Resolution 5 / ADR 4).
/// </summary>
internal static class BrandManualJsonOptions
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
