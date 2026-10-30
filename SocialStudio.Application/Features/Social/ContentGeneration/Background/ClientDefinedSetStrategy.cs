using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// CASE A strategy: client-defined background SET with carousel-coherent deterministic rotation.
/// Rules:
///   - 0 backgrounds → Creative for all slides (no set configured).
///   - 1 background  → strict: same References([single]) for every slide (BSP-02).
///   - N backgrounds → deterministic rotation: slide i → References([set[stableHash(seed,i) % N]])
///                     guaranteeing coherence and reproducibility on retry (BSP-01a/c).
/// Refs: design §4.3; BSP-01/02.
/// </summary>
public sealed class ClientDefinedSetStrategy : IBackgroundSourcingStrategy
{
    /// <inheritdoc />
    public BackgroundSourcingPolicy Policy => BackgroundSourcingPolicy.ClientDefinedSet;

    /// <inheritdoc />
    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        var set = BuildEligibleSet(request.Brand.BackgroundTemplates, request.FormatKey);
        var perSlide = new BackgroundSelection[request.SlideCount];

        if (set.Count == 0)
        {
            // No backgrounds configured for this client — fall back to creative generation
            for (var i = 0; i < request.SlideCount; i++)
            {
                perSlide[i] = new BackgroundSelection.Creative();
            }
        }
        else if (set.Count == 1)
        {
            // Strict: the single background applies to every slide (BSP-02)
            for (var i = 0; i < request.SlideCount; i++)
            {
                perSlide[i] = new BackgroundSelection.References(new[] { set[0] });
            }
        }
        else
        {
            // Deterministic rotation: same seed → same assignment on retry (BSP-01a/c)
            for (var i = 0; i < request.SlideCount; i++)
            {
                // slideIndex is 1-based for StableHash to match ForSlide convention
                var slotIndex = CarouselBackgroundPlan.StableHash(request.SeedToken, i + 1) % set.Count;
                perSlide[i] = new BackgroundSelection.References(new[] { set[slotIndex] });
            }
        }

        return new CarouselBackgroundPlan(Policy, perSlide, request.SeedToken);
    }

    /// <summary>
    /// Builds the eligible background set for the given format key.
    /// All non-ForcedDefault templates that apply to this format (or all formats) are included.
    /// ForcedDefault entries are excluded — they are served by LegacyTierStrategy.
    /// </summary>
    private static IReadOnlyList<BrandBackgroundTemplateSnapshot> BuildEligibleSet(
        IReadOnlyList<BrandBackgroundTemplateSnapshot> templates,
        string formatKey)
    {
        return templates
            .Where(t => t.Tier != BackgroundTier.ForcedDefault)
            .Where(t => string.IsNullOrWhiteSpace(t.FormatKey)
                || string.Equals(t.FormatKey, formatKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
