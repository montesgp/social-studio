using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Stateless tier-priority selector for brand background templates (BG-03).
/// Priority: Tier-1 ForcedDefault (exact format match) > Tier-2 References > Tier-3 Creative.
/// </summary>
public static class BackgroundSelector
{
    /// <summary>
    /// Selects the appropriate background for the given <paramref name="formatKey"/>
    /// using the 3-tier priority model.
    /// </summary>
    public static BackgroundSelection Select(
        IReadOnlyList<BrandBackgroundTemplateSnapshot> templates,
        string formatKey)
    {
        // Tier-1: a ForcedDefault entry that explicitly targets this format.
        var forced = templates.FirstOrDefault(t =>
            t.Tier == BackgroundTier.ForcedDefault
            && t.IsFormatDefault
            && string.Equals(t.FormatKey, formatKey, StringComparison.OrdinalIgnoreCase));

        if (forced is not null)
        {
            return new BackgroundSelection.ForcedDefault(forced.Url, forced.Name);
        }

        // Tier-2: all Reference entries (format-agnostic style guides).
        var references = templates
            .Where(t => t.Tier == BackgroundTier.Reference)
            .ToArray();

        if (references.Length > 0)
        {
            return new BackgroundSelection.References(references);
        }

        // Tier-3: free creative generation, brand-bound.
        return new BackgroundSelection.Creative();
    }
}
