using System.Text.Json;

namespace SocialStudio.Infrastructure.Social.Providers;

/// <summary>
/// Reads actor ids out of an organization's <c>actor_mappings_json</c> credential for a given
/// <c>(networkKey, variantKey)</c> pair. Extracted out of <see cref="ApifyProfileScrapeResolver"/>
/// (which now delegates here, keeping its existing tests green) so <c>ApifyNicheSearchResolver</c> can
/// reuse the exact same configuration-reading contract rather than re-implementing it (CD-12/D13: one
/// pattern, no second way to read the same data).
///
/// There is NO "default" fallback. Entries with variantKey == "default" are NOT used. The startup
/// backfill (<c>ApifyActorMappingBackfill</c>) re-tags legacy "default" entries to "profile" and
/// "social-extraction" — it never produces "niche-search" (INT-DISC-04) — before this method is called
/// in production. If no matching entries exist, this returns empty and the caller returns a structured
/// "not configured" outcome; never a substituted literal.
/// </summary>
internal static class ApifyActorMappingLookup
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns ALL actor ids whose (networkKey, variantKey) pair matches the request, with the primary
    /// actor (IsPrimary == true) returned first, followed by the remaining matches in JSON array order
    /// (backups). Tolerates JSON entries missing the isPrimary field (defaults to false).
    /// </summary>
    internal static IReadOnlyList<string> GetAllMappedActors(
        string actorMappingsJson,
        string networkKey,
        string variantKey)
    {
        try
        {
            var mappings = JsonSerializer.Deserialize<List<ApifyActorMappingEntry>>(
                actorMappingsJson, JsonOptions) ?? [];

            var matches = mappings
                .Where(m =>
                    !string.IsNullOrWhiteSpace(m.ActorId)
                    && Matches(m.NetworkKey, networkKey)
                    && Matches(m.VariantKey, variantKey))
                .ToList();

            if (matches.Count == 0)
            {
                return [];
            }

            // Primary first, then the remaining in original array order.
            // OrderBy is stable: non-primary entries preserve their relative JSON order.
            return matches
                .OrderByDescending(m => m.IsPrimary)
                .Select(m => m.ActorId.Trim())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool Matches(string? value, string expected) =>
        string.Equals(value?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record ApifyActorMappingEntry(
        string NetworkKey,
        string VariantKey,
        string ActorId,
        bool IsPrimary = false);
}
