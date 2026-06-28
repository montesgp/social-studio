using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// RED tests for WU-5, task 5.5:
/// CarouselVisualComposer gains an @handle/signature copy slot at parity with StoryVisualComposer.
/// Refs: CAC-CG-03; design §2.1, §2.3.
/// </summary>
public sealed class CarouselVisualComposerHandleSlotTests
{
    private static readonly BrandManualSnapshot EmptyBrand = new(
        ColorPalette: Array.Empty<WeightedColor>(),
        Logos: Array.Empty<string>(),
        BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
        Typography: Array.Empty<string>(),
        VoiceAndTone: null,
        Notes: null);

    // --- CAC-CG-03-a: Handle present → @handle slot carries the handle value ---

    /// <summary>
    /// GIVEN BrandSnapshot with ClientProfileSnapshot.Handle = "@clientbrand",
    /// WHEN CarouselVisualComposer.ComposeMany is called,
    /// THEN every slide's CopySlots contains "@handle" = "@clientbrand".
    /// </summary>
    [Fact]
    public void ComposeMany_WithHandlePresent_AddsAtHandleSlotToEverySlide()
    {
        // Arrange
        var client = new ClientProfileSnapshot("Client Brand", null, null, Handle: "@clientbrand");
        var brand = BrandSnapshot.FromBrandData(EmptyBrand, null, null, client: client);
        var payload = BuildCarouselPayload(3);
        var composer = new CarouselVisualComposer();
        var inferred = new InferenceAccumulator();

        // Act
        var specs = composer.ComposeMany(payload, brand, inferred);

        // Assert — every slide must carry @handle slot with the handle value (CAC-CG-03-a).
        Assert.All(specs, spec =>
        {
            Assert.True(spec.CopySlots.ContainsKey("@handle"),
                "Expected '@handle' key in CopySlots when Handle is non-null.");
            Assert.Equal("@clientbrand", spec.CopySlots["@handle"]);
        });
    }

    // --- CAC-CG-03-b: Handle null → @handle slot absent; composition succeeds ---

    /// <summary>
    /// GIVEN BrandSnapshot with ClientProfileSnapshot.Handle = null,
    /// WHEN CarouselVisualComposer.ComposeMany is called,
    /// THEN no @handle key appears in any slide's CopySlots, and no exception is raised.
    /// </summary>
    [Fact]
    public void ComposeMany_WithHandleNull_OmitsAtHandleSlot_AndSucceeds()
    {
        // Arrange — Handle defaults to null (no own-account handle).
        var client = new ClientProfileSnapshot("Client Brand", null, null, Handle: null);
        var brand = BrandSnapshot.FromBrandData(EmptyBrand, null, null, client: client);
        var payload = BuildCarouselPayload(3);
        var composer = new CarouselVisualComposer();
        var inferred = new InferenceAccumulator();

        // Act — must not throw.
        var exception = Record.Exception(() => composer.ComposeMany(payload, brand, inferred));
        var specs = composer.ComposeMany(payload, brand, inferred);

        // Assert — no exception, @handle key must be absent (not empty string, just absent).
        Assert.Null(exception);
        Assert.NotEmpty(specs);
        Assert.All(specs, spec =>
        {
            Assert.False(spec.CopySlots.ContainsKey("@handle"),
                "@handle slot must be omitted (not empty) when Handle is null.");
        });
    }

    // --- No client snapshot at all → slot absent (cold start / inferred) ---

    [Fact]
    public void ComposeMany_WithNoClientSnapshot_OmitsAtHandleSlot()
    {
        // Arrange — no client at all (cold start; BrandSnapshot.Client is null).
        var brand = BrandSnapshot.FromBrandData(EmptyBrand, null, null, client: null);
        var payload = BuildCarouselPayload(2);
        var composer = new CarouselVisualComposer();
        var inferred = new InferenceAccumulator();

        var specs = composer.ComposeMany(payload, brand, inferred);

        Assert.All(specs, spec =>
        {
            Assert.False(spec.CopySlots.ContainsKey("@handle"));
        });
    }

    // --- Plan-aware overload also carries the handle slot ---

    [Fact]
    public void ComposeMany_WithPlanAndHandlePresent_AddsAtHandleSlot()
    {
        var client = new ClientProfileSnapshot("Plan Brand", null, null, Handle: "@planbrand");
        var brand = BrandSnapshot.FromBrandData(EmptyBrand, null, null, client: client);
        var payload = BuildCarouselPayload(2);
        var composer = new CarouselVisualComposer();
        var planAware = (IBackgroundPlanAwareComposer)composer;
        var inferred = new InferenceAccumulator();

        var perSlide = Enumerable.Range(0, 2)
            .Select(_ => (BackgroundSelection)new BackgroundSelection.Creative())
            .ToList<BackgroundSelection>();
        var plan = new CarouselBackgroundPlan(
            Incoders.Hive.Domain.Social.Clients.BackgroundSourcingPolicy.AiPalette,
            perSlide,
            "test-seed");

        var specs = planAware.ComposeMany(payload, brand, inferred, plan);

        Assert.All(specs, spec =>
        {
            Assert.True(spec.CopySlots.ContainsKey("@handle"));
            Assert.Equal("@planbrand", spec.CopySlots["@handle"]);
        });
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CarouselContent BuildCarouselPayload(int slideCount)
    {
        var slides = Enumerable.Range(1, slideCount)
            .Select(i => new CarouselSlide($"Slide {i}", $"Body {i}", null))
            .ToList();
        return new CarouselContent(
            CoverTitle: "Handle Test Carousel",
            Slides: slides,
            CtaText: "Get Started",
            CtaLink: null);
    }
}
