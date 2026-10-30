using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// WU-4 C5 TDD tests: CarouselVisualComposer consumes the CarouselBackgroundPlan.
///
/// After the refactor (task 4.7):
/// - When a plan is provided, each slide's AssetSlots["slide-N"].ImageUrl matches plan.ForSlide(N).
/// - No independent BackgroundSelector.Select call is made inside the composer.
/// - Without a plan (legacy path), the composer falls back to BackgroundSelector.Select per slide.
///
/// Refs: CG-COH-02; design §7.2; tasks 4.6/4.7.
/// </summary>
public sealed class CarouselVisualComposerPlanTests
{
    private static readonly string FormatKey = "carousel";

    private static readonly BrandManualSnapshot EmptyBrand = new(
        ColorPalette: Array.Empty<WeightedColor>(),
        Logos: Array.Empty<string>(),
        BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
        Typography: Array.Empty<string>(),
        VoiceAndTone: null,
        Notes: null);

    private static readonly BrandSnapshot EmptyBrandSnapshot =
        BrandSnapshot.FromBrandData(EmptyBrand, null, null);

    // -----------------------------------------------------------------------
    // C5/RED: slide slot URL comes from plan.ForSlide(i), not BackgroundSelector.Select
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN a pre-built plan with CASE-A rotation (3 distinct backgrounds, 3 slides),
    /// WHEN ComposeMany is called via IBackgroundPlanAwareComposer,
    /// THEN each slide's AssetSlots["slide-N"].ImageUrl equals plan.ForSlide(N)'s URL.
    /// The composer must NOT call BackgroundSelector.Select independently.
    /// Refs: design §7.2; CG-COH-02.
    /// </summary>
    [Fact]
    public void ComposeMany_WithPlan_EachSlideImageUrlMatchesPlanForSlide()
    {
        // Arrange — 3 distinct templates, 3-slide CASE-A plan
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg2", "https://cdn/bg2.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg3", "https://cdn/bg3.jpg", null, BackgroundTier.Creative),
        };

        var seedToken = "test-seed-001";
        var slideCount = 3;

        // Build the plan explicitly (same logic as ClientDefinedSetStrategy)
        var perSlide = Enumerable.Range(1, slideCount)
            .Select(i =>
            {
                var idx = CarouselBackgroundPlan.StableHash(seedToken, i) % templates.Length;
                return (BackgroundSelection)new BackgroundSelection.References(new[] { templates[idx] });
            })
            .ToList<BackgroundSelection>();
        var plan = new CarouselBackgroundPlan(
            BackgroundSourcingPolicy.ClientDefinedSet, perSlide, seedToken);

        var composer = new CarouselVisualComposer();
        var planAware = (IBackgroundPlanAwareComposer)composer;
        var payload = BuildCarouselPayload(slideCount);
        var inferred = new InferenceAccumulator();

        // Act
        var specs = planAware.ComposeMany(payload, EmptyBrandSnapshot, inferred, plan);

        // Assert — each slide's image URL must match the plan entry
        Assert.Equal(slideCount, specs.Count);
        for (var i = 0; i < slideCount; i++)
        {
            var expected = plan.ForSlide(i + 1);
            var expectedUrl = expected is BackgroundSelection.References refs
                ? refs.Entries.FirstOrDefault()?.Url
                : null;

            var slideSlot = specs[i].AssetSlots.First(kv => kv.Key.StartsWith("slide-"));
            Assert.Equal(expectedUrl, slideSlot.Value.ImageUrl);
        }
    }

    /// <summary>
    /// GIVEN a CASE-B plan (all slides Creative → null URL),
    /// WHEN ComposeMany is called with the plan,
    /// THEN all slide slots have null ImageUrl (no template enforcement for AI-generated backgrounds).
    /// </summary>
    [Fact]
    public void ComposeMany_WithCreativePlan_AllSlotsHaveNullImageUrl()
    {
        var slideCount = 4;
        var perSlide = Enumerable.Range(0, slideCount)
            .Select(_ => (BackgroundSelection)new BackgroundSelection.Creative())
            .ToList<BackgroundSelection>();
        var plan = new CarouselBackgroundPlan(
            BackgroundSourcingPolicy.AiPalette, perSlide, "seed-b");

        var composer = new CarouselVisualComposer();
        var planAware = (IBackgroundPlanAwareComposer)composer;
        var payload = BuildCarouselPayload(slideCount);
        var inferred = new InferenceAccumulator();

        var specs = planAware.ComposeMany(payload, EmptyBrandSnapshot, inferred, plan);

        Assert.Equal(slideCount, specs.Count);
        foreach (var spec in specs)
        {
            var slideSlot = spec.AssetSlots.First(kv => kv.Key.StartsWith("slide-"));
            Assert.Null(slideSlot.Value.ImageUrl);
        }
    }

    /// <summary>
    /// GIVEN a CASE-A plan with a single background (strict: same for every slide),
    /// WHEN ComposeMany is called with the plan,
    /// THEN ALL slides have the same ImageUrl (the single background).
    /// Refs: BSP-02 (strict single-background enforcement).
    /// </summary>
    [Fact]
    public void ComposeMany_SingleBackgroundPlan_AllSlidesHaveSameUrl()
    {
        var template = new BrandBackgroundTemplateSnapshot(
            "bg-single", "https://cdn/bg-single.jpg", null, BackgroundTier.Creative);
        var slideCount = 3;

        // Single background → every slide gets References([template])
        var perSlide = Enumerable.Range(0, slideCount)
            .Select(_ => (BackgroundSelection)new BackgroundSelection.References(new[] { template }))
            .ToList<BackgroundSelection>();
        var plan = new CarouselBackgroundPlan(
            BackgroundSourcingPolicy.ClientDefinedSet, perSlide, "seed-single");

        var composer = new CarouselVisualComposer();
        var planAware = (IBackgroundPlanAwareComposer)composer;
        var payload = BuildCarouselPayload(slideCount);
        var inferred = new InferenceAccumulator();

        var specs = planAware.ComposeMany(payload, EmptyBrandSnapshot, inferred, plan);

        Assert.Equal(slideCount, specs.Count);
        foreach (var spec in specs)
        {
            var slideSlot = spec.AssetSlots.First(kv => kv.Key.StartsWith("slide-"));
            Assert.Equal("https://cdn/bg-single.jpg", slideSlot.Value.ImageUrl);
        }
    }

    /// <summary>
    /// GIVEN a legacy call without a plan,
    /// WHEN ComposeMany is called via IMultiAssetVisualComposer (legacy path),
    /// THEN the composer falls back to BackgroundSelector.Select — no crash.
    /// This preserves backward compatibility for paths that do not supply a plan.
    /// </summary>
    [Fact]
    public void ComposeMany_WithoutPlan_FallsBackToBackgroundSelector()
    {
        var slideCount = 2;
        var composer = new CarouselVisualComposer();
        var multi = (IMultiAssetVisualComposer)composer;
        var payload = BuildCarouselPayload(slideCount);
        var inferred = new InferenceAccumulator();

        // Empty templates → BackgroundSelector returns Creative → null ImageUrl
        var specs = multi.ComposeMany(payload, EmptyBrandSnapshot, inferred);

        Assert.Equal(slideCount, specs.Count);
        foreach (var spec in specs)
        {
            var slideSlot = spec.AssetSlots.First(kv => kv.Key.StartsWith("slide-"));
            Assert.Null(slideSlot.Value.ImageUrl); // Creative → no URL
        }
    }

    /// <summary>
    /// Verify that CarouselVisualComposer implements IBackgroundPlanAwareComposer.
    /// This is the contract that VisualCompositionService checks at runtime to route
    /// the plan through to the composer. If this fails, the plan is silently dropped.
    /// </summary>
    [Fact]
    public void CarouselVisualComposer_ImplementsIBackgroundPlanAwareComposer()
    {
        var composer = new CarouselVisualComposer();
        Assert.IsAssignableFrom<IBackgroundPlanAwareComposer>(composer);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CarouselContent BuildCarouselPayload(int slideCount)
    {
        var slides = Enumerable.Range(1, slideCount)
            .Select(i => new CarouselSlide($"Slide {i} Title", $"Body {i}", null))
            .ToList();
        return new CarouselContent(
            CoverTitle: "Test Carousel",
            Slides: slides,
            CtaText: "Learn More",
            CtaLink: null);
    }
}
