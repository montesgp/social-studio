using SocialStudio.Domain.Social.ClientBrandManuals;

namespace SocialStudio.Domain.Social.ContentGeneration;

/// <summary>
/// Stateless tier-priority selector for brand background templates (BG-03).
/// Priority: Tier-1 ForcedDefault (exact format match) > Tier-2 manual templates/references >
/// Tier-3 Creative. Product rule: when the client has an applicable background template in the
/// manual, generation uses that template as the background instead of inventing a free one.
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

        // Tier-2: applicable manual templates. This intentionally includes legacy/default
        // Creative-tier rows because older stored brand manuals did not classify templates yet;
        // if the user defined an image in the manual, it is still a usable template.
        var references = templates
            .Where(t => t.Tier != BackgroundTier.ForcedDefault)
            .Where(t => string.IsNullOrWhiteSpace(t.FormatKey)
                || string.Equals(t.FormatKey, formatKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (references.Length > 0)
        {
            return new BackgroundSelection.References(references);
        }

        // Tier-3: free creative generation, brand-bound.
        return new BackgroundSelection.Creative();
    }
}
