using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for WU-3 step 3.7: AiPaletteStrategy (CASE B).
/// Refs: BSP-03; design §4.3; tasks 3.7.
/// </summary>
public sealed class AiPaletteStrategyTests
{
    // -----------------------------------------------------------------------
    // Policy
    // -----------------------------------------------------------------------

    [Fact]
    public void Policy_IsAiPalette()
    {
        var strategy = new AiPaletteStrategy();

        Assert.Equal(BackgroundSourcingPolicy.AiPalette, strategy.Policy);
    }

    // -----------------------------------------------------------------------
    // BSP-03b: every slide is Creative (AI designs freely using palette weighting)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_AllSlides_AreCreative()
    {
        var brand = BuildBrandWithBackgrounds();
        var request = new BackgroundPlanRequest(brand, "carousel", SlideCount: 5, SeedToken: "seed", Array.Empty<string>());
        var strategy = new AiPaletteStrategy();

        var plan = strategy.Plan(request);

        Assert.Equal(5, plan.PerSlide.Count);
        Assert.All(plan.PerSlide, s => Assert.IsType<BackgroundSelection.Creative>(s));
    }

    [Fact]
    public void Plan_SingleSlide_IsCreative()
    {
        var brand = BuildBrandWithBackgrounds();
        var request = new BackgroundPlanRequest(brand, "story-single", SlideCount: 1, SeedToken: "seed", Array.Empty<string>());
        var strategy = new AiPaletteStrategy();

        var plan = strategy.Plan(request);

        Assert.Single(plan.PerSlide);
        Assert.IsType<BackgroundSelection.Creative>(plan.PerSlide[0]);
    }

    // -----------------------------------------------------------------------
    // Strategy carries no reference to background sets (BSP-03a)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_IgnoresBackgroundTemplates_StillAllCreative()
    {
        // Even with templates present, CASE B always produces Creative
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
        };
        var brand = new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: templates,
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);

        var request = new BackgroundPlanRequest(brand, "carousel", SlideCount: 3, SeedToken: "seed", Array.Empty<string>());
        var strategy = new AiPaletteStrategy();

        var plan = strategy.Plan(request);

        Assert.All(plan.PerSlide, s => Assert.IsType<BackgroundSelection.Creative>(s));
    }

    private static BrandManualSnapshot BuildBrandWithBackgrounds()
    {
        return new BrandManualSnapshot(
            ColorPalette: new[] { new WeightedColor("#FF0000", null, 0) },
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);
    }
}
