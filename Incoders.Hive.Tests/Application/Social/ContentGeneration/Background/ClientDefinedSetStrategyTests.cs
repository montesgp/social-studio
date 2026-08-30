using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for WU-3 step 3.5: ClientDefinedSetStrategy (CASE A).
/// Refs: BSP-01, BSP-02; design §4.3; tasks 3.5.
/// </summary>
public sealed class ClientDefinedSetStrategyTests
{
    private static readonly string FormatKey = "carousel";

    // -----------------------------------------------------------------------
    // Policy
    // -----------------------------------------------------------------------

    [Fact]
    public void Policy_IsClientDefinedSet()
    {
        var strategy = new ClientDefinedSetStrategy();

        Assert.Equal(BackgroundSourcingPolicy.ClientDefinedSet, strategy.Policy);
    }

    // -----------------------------------------------------------------------
    // setCount = 0 → Creative all slides (no backgrounds configured)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_EmptySet_AllSlidesAreCreative()
    {
        var brand = BuildBrand(templates: Array.Empty<BrandBackgroundTemplateSnapshot>());
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 4, SeedToken: "seed", Array.Empty<string>());
        var strategy = new ClientDefinedSetStrategy();

        var plan = strategy.Plan(request);

        Assert.Equal(4, plan.PerSlide.Count);
        Assert.All(plan.PerSlide, s => Assert.IsType<BackgroundSelection.Creative>(s));
    }

    // -----------------------------------------------------------------------
    // setCount = 1 → strict: every slide references the single background (BSP-02)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_SingleBackground_AllSlidesReferenceSameBackground()
    {
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
        };
        var brand = BuildBrand(templates);
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 5, SeedToken: "seed", Array.Empty<string>());
        var strategy = new ClientDefinedSetStrategy();

        var plan = strategy.Plan(request);

        Assert.Equal(5, plan.PerSlide.Count);
        foreach (var slide in plan.PerSlide)
        {
            var refs = Assert.IsType<BackgroundSelection.References>(slide);
            Assert.Single(refs.Entries);
            Assert.Equal("bg1", refs.Entries[0].Name);
        }
    }

    // -----------------------------------------------------------------------
    // setCount > 1 → deterministic rotation (BSP-01a, BSP-01c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_MultipleBackgrounds_RotationIsDeterministic_SameSeed()
    {
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg2", "https://cdn/bg2.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg3", "https://cdn/bg3.jpg", null, BackgroundTier.Creative),
        };
        var brand = BuildBrand(templates);
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 4, SeedToken: "job-J1", Array.Empty<string>());
        var strategy = new ClientDefinedSetStrategy();

        var plan1 = strategy.Plan(request);
        var plan2 = strategy.Plan(request);

        Assert.Equal(4, plan1.PerSlide.Count);
        Assert.Equal(4, plan2.PerSlide.Count);

        for (var i = 0; i < 4; i++)
        {
            var refs1 = Assert.IsType<BackgroundSelection.References>(plan1.PerSlide[i]);
            var refs2 = Assert.IsType<BackgroundSelection.References>(plan2.PerSlide[i]);
            Assert.Equal(refs1.Entries[0].Name, refs2.Entries[0].Name);
        }
    }

    [Fact]
    public void Plan_MultipleBackgrounds_NotAllSlidesIdentical_WhenSetCountGt1()
    {
        // With 3 backgrounds and 6 slides, at least 2 different backgrounds must appear
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg2", "https://cdn/bg2.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg3", "https://cdn/bg3.jpg", null, BackgroundTier.Creative),
        };
        var brand = BuildBrand(templates);
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 6, SeedToken: "job-variety", Array.Empty<string>());
        var strategy = new ClientDefinedSetStrategy();

        var plan = strategy.Plan(request);

        var distinctNames = plan.PerSlide
            .Cast<BackgroundSelection.References>()
            .Select(r => r.Entries[0].Name)
            .Distinct()
            .Count();

        Assert.True(distinctNames > 1,
            "Expected deterministic rotation to assign different backgrounds across 6 slides when 3 are available");
    }

    // -----------------------------------------------------------------------
    // SeedToken is preserved in the plan
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_SeedToken_IsPreservedInPlan()
    {
        var brand = BuildBrand(Array.Empty<BrandBackgroundTemplateSnapshot>());
        var request = new BackgroundPlanRequest(brand, FormatKey, SlideCount: 2, SeedToken: "job-xyz", Array.Empty<string>());
        var strategy = new ClientDefinedSetStrategy();

        var plan = strategy.Plan(request);

        Assert.Equal("job-xyz", plan.SeedToken);
    }

    // -----------------------------------------------------------------------
    // Helper: build brand snapshot from template list
    // -----------------------------------------------------------------------

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
