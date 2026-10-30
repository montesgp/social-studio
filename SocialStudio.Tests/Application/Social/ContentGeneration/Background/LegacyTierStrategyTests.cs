using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Background;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for WU-3 step 3.3: LegacyTierStrategy parity with BackgroundSelector.
/// Refs: BSP-05, design §4.3 (LegacyTierStrategy row); tasks 3.3.
/// </summary>
public sealed class LegacyTierStrategyTests
{
    private static readonly string FormatKey = "carousel";

    // -----------------------------------------------------------------------
    // Policy property
    // -----------------------------------------------------------------------

    [Fact]
    public void Policy_IsUnset()
    {
        var strategy = new LegacyTierStrategy();

        Assert.Equal(BackgroundSourcingPolicy.Unset, strategy.Policy);
    }

    // -----------------------------------------------------------------------
    // Parity: Plan per-slide matches BackgroundSelector.Select for same inputs
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_NoTemplates_AllSlidesAreCreative_MatchingSelector()
    {
        var brand = BuildBrand(templates: Array.Empty<BrandBackgroundTemplateSnapshot>());
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 3, SeedToken: "seed", Array.Empty<string>());
        var strategy = new LegacyTierStrategy();

        var plan = strategy.Plan(request);

        // BackgroundSelector would return Creative for an empty template set
        var expected = BackgroundSelector.Select(Array.Empty<BrandBackgroundTemplateSnapshot>(), FormatKey);
        Assert.Equal(3, plan.PerSlide.Count);
        foreach (var slide in plan.PerSlide)
        {
            Assert.IsType<BackgroundSelection.Creative>(slide);
        }
        Assert.IsType<BackgroundSelection.Creative>(expected);
    }

    [Fact]
    public void Plan_WithReferences_AllSlidesBuildReferences_MatchingSelector()
    {
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg2", "https://cdn/bg2.jpg", null, BackgroundTier.Creative),
        };
        var brand = BuildBrand(templates);
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 2, SeedToken: "seed", Array.Empty<string>());
        var strategy = new LegacyTierStrategy();

        var plan = strategy.Plan(request);

        var expected = BackgroundSelector.Select(templates, FormatKey);

        Assert.Equal(2, plan.PerSlide.Count);
        foreach (var slide in plan.PerSlide)
        {
            // Each slide must be a References (same type as selector result)
            var refs = Assert.IsType<BackgroundSelection.References>(slide);
            var expectedRefs = Assert.IsType<BackgroundSelection.References>(expected);
            Assert.Equal(expectedRefs.Entries.Count, refs.Entries.Count);
        }
    }

    [Fact]
    public void Plan_WithForcedDefault_AllSlidesForcedDefault_MatchingSelector()
    {
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot(
                "Forced BG", "https://cdn/forced.jpg", null,
                BackgroundTier.ForcedDefault,
                FormatKey: FormatKey,
                IsFormatDefault: true),
        };
        var brand = BuildBrand(templates);
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 4, SeedToken: "seed", Array.Empty<string>());
        var strategy = new LegacyTierStrategy();

        var plan = strategy.Plan(request);

        var expected = BackgroundSelector.Select(templates, FormatKey);

        Assert.Equal(4, plan.PerSlide.Count);
        foreach (var slide in plan.PerSlide)
        {
            var fd = Assert.IsType<BackgroundSelection.ForcedDefault>(slide);
            var expectedFd = Assert.IsType<BackgroundSelection.ForcedDefault>(expected);
            Assert.Equal(expectedFd.Url, fd.Url);
            Assert.Equal(expectedFd.Name, fd.Name);
        }
    }

    [Fact]
    public void Plan_SingleSlide_MatchesSelector()
    {
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
        };
        var brand = BuildBrand(templates);
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 1, SeedToken: "seed", Array.Empty<string>());
        var strategy = new LegacyTierStrategy();

        var plan = strategy.Plan(request);

        var expected = BackgroundSelector.Select(templates, FormatKey);

        Assert.Single(plan.PerSlide);
        Assert.Equal(expected.GetType(), plan.PerSlide[0].GetType());
    }

    [Fact]
    public void Plan_SeedToken_IsPreservedInPlan()
    {
        var brand = BuildBrand(templates: Array.Empty<BrandBackgroundTemplateSnapshot>());
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 1, SeedToken: "job-42", Array.Empty<string>());
        var strategy = new LegacyTierStrategy();

        var plan = strategy.Plan(request);

        Assert.Equal("job-42", plan.SeedToken);
    }

    private static BrandManualSnapshot BuildBrand(IReadOnlyList<BrandBackgroundTemplateSnapshot> templates)
    {
        return new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: templates,
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);
    }
}
