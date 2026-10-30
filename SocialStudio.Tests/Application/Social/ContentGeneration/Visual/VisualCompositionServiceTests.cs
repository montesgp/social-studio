using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Story;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// TDD tests for <see cref="VisualCompositionService"/>.
/// Verifies per-format composition spec generation with and without brand data.
/// </summary>
public sealed class VisualCompositionServiceTests
{
    // -------------------------------------------------------------------
    // Carousel + full brand → uses brand palette and typography
    // -------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_Carousel_FullBrand_UsesBrandPaletteAndTypography()
    {
        var service = CreateService();
        var brand = BuildFullBrand();
        var payload = new { coverTitle = "5 Tips", slides = Array.Empty<object>() };

        var spec = await service.ComposeAsync("carousel", payload, brand, CancellationToken.None);

        Assert.Equal("carousel", spec.FormatKey);
        // Primary color from brand palette
        Assert.Equal("#FF5733", spec.Palette.Primary);
        // Heading font from brand typography
        Assert.Equal("Roboto", spec.Typography.HeadingFamily);
        Assert.Empty(service.GetInferredFields());
    }

    // -------------------------------------------------------------------
    // Carousel + missing brand → default palette, inferredFields populated
    // -------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_Carousel_MissingBrand_UsesDefaultPaletteAndPopulatesInferredFields()
    {
        var service = CreateService();
        var brand = BrandSnapshot.FromBrandData(null, null, null); // fully inferred
        var payload = new { coverTitle = "Brand Tips" };

        var spec = await service.ComposeAsync("carousel", payload, brand, CancellationToken.None);

        Assert.Equal("carousel", spec.FormatKey);
        // Default neutral palette
        Assert.Equal(VisualCompositionService.DefaultPalette.Primary, spec.Palette.Primary);
        // InferredFields should contain brand palette key
        var inferred = service.GetInferredFields();
        Assert.Contains("brand.ColorPalette", inferred);
    }

    // -------------------------------------------------------------------
    // Story + brand → story-specific layout
    // -------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_Story_FullBrand_ProducesStoryLayout()
    {
        var service = CreateService();
        // Issue #70: the @handle/model signature is opt-in — SignsOwnSlides must be true for
        // AttributionPolicy.ResolveSignature to emit the payload-provided "@client" signature.
        // Name/Handle left blank so the deterministic fallback chain doesn't shadow the
        // payload-provided model signature this test exercises.
        var brand = BuildFullBrand() with
        {
            Client = new ClientProfileSnapshot(" ", null, null, SignsOwnSlides: true),
        };
        var payload = new StoryContent(
            "Grand Opening",
            "This week only",
            "A story based on the selected idea.",
            ["Brand-safe", "Story-ready"],
            "Book now",
            "Product-led branded background",
            "@client",
            null);

        var spec = await service.ComposeAsync("story", payload, brand, CancellationToken.None);

        Assert.Equal("story-single", spec.FormatKey);
        Assert.Equal(1080, spec.Dimensions.Width);
        Assert.Equal(1920, spec.Dimensions.Height);
        // Product rule: if the client has a background image in the manual, use it as the
        // template/background. Legacy/default Creative-tier rows still count as manual templates.
        Assert.Equal("/storage/client-assets/org/client/story.png", spec.AssetSlots["background"].ImageUrl);
        Assert.Equal("@client", spec.CopySlots["signature"]);
    }

    // -------------------------------------------------------------------
    // Hook, Caption, CtaVariation are text-only → single text-card slot
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("hook")]
    [InlineData("caption")]
    [InlineData("cta-variation")]
    public async Task ComposeAsync_TextOnlyFormats_ProduceTextCardAssetSlot(string formatKey)
    {
        var service = CreateService();
        var brand = BuildFullBrand();

        var spec = await service.ComposeAsync(formatKey, new { text = "Copy text" }, brand, CancellationToken.None);

        Assert.Equal(formatKey, spec.FormatKey);
        Assert.True(spec.AssetSlots.ContainsKey("text-card"),
            $"Expected 'text-card' slot in {formatKey} but got: {string.Join(", ", spec.AssetSlots.Keys)}");
    }

    // -------------------------------------------------------------------
    // ReelScript → script-document slot
    // -------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_ReelScript_ProducesScriptDocumentSlot()
    {
        var service = CreateService();
        var spec = await service.ComposeAsync("reel-script", new { }, BuildFullBrand(), CancellationToken.None);

        Assert.Equal("reel-script", spec.FormatKey);
        Assert.True(spec.AssetSlots.ContainsKey("script-document"));
    }

    // -------------------------------------------------------------------
    // ContentIdea → summary-card slot
    // -------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_ContentIdea_ProducesSummaryCardSlot()
    {
        var service = CreateService();
        var spec = await service.ComposeAsync("content-idea", new { }, BuildFullBrand(), CancellationToken.None);

        Assert.Equal("content-idea", spec.FormatKey);
        Assert.True(spec.AssetSlots.ContainsKey("summary-card"));
    }

    // -------------------------------------------------------------------
    // GitHub issue #81 regression: ComposeManyAsync through the ACTUAL IVisualCompositionService
    // port must thread a ClientAestheticProfile's real observed typography into the composed spec
    // even when a CarouselBackgroundPlan is ALSO supplied (the production call shape —
    // ContentGenerationJobProcessor always supplies a plan for carousel formats). Before the fix,
    // VisualCompositionService dispatched plan-aware composers WITHOUT the profile, so a
    // ClientAesthetic carousel's real family/weight never reached the rendered markup.
    // -------------------------------------------------------------------

    [Fact]
    public async Task ComposeManyAsync_PlanAndProfileBothSupplied_UsesProfileTypography_NotGenericDefault()
    {
        IVisualCompositionService service = CreateService();
        var brand = BuildFullBrand();
        // PayloadReader.As<T> only recognizes an already-typed instance or a JsonElement (never an
        // anonymous object) — CarouselContent constructed directly so the composer's slide-count
        // guard actually resolves through ComposeManyCore instead of degrading to the empty fallback.
        var payload = new CarouselContent(
            CoverTitle: "5 Tips",
            Slides: [new CarouselSlide("Slide 1", "Body 1", null)],
            CtaText: "Learn more",
            CtaLink: null);

        // A real client-observed typography signal, distinct from the brand's "Roboto" fallback.
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            Typography:
            [
                new AestheticTypography("heading", "Cormorant Garamond", Weight: 550, SizeRatio: 0.1),
            ]);

        // The production call shape: a non-null plan supplied ALONGSIDE the profile. Before the
        // fix, VisualCompositionService's plan-aware dispatch branch ignored `aestheticProfile`
        // entirely whenever a plan was present — this is the exact production call site's shape.
        var plan = new CarouselBackgroundPlan(
            BackgroundSourcingPolicy.ClientAesthetic,
            [(BackgroundSelection)new BackgroundSelection.Creative()],
            SeedToken: "seed");

        var specs = await service.ComposeManyAsync(
            "carousel", payload, brand, CancellationToken.None, plan, profile);

        Assert.NotEmpty(specs);
        var typography = specs[0].Typography;
        // FontFamilyResolution substitutes an actual bundled font for "Cormorant Garamond" (a serif
        // renderable, e.g. "Liberation Serif") — the assertion targets the OBSERVABLE symptom from
        // the field report: real typography reached the spec, NOT the generic brand/default family.
        Assert.NotEqual("Roboto", typography.HeadingFamily);
        Assert.Equal(550, typography.MeasuredHeadingWeight);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static VisualCompositionService CreateService() => new();

    private static BrandSnapshot BuildFullBrand() => BrandSnapshot.FromBrandData(
        brand: new BrandManualSnapshot(
            ColorPalette:
            [
                new WeightedColor("#FF5733", ColorRole.Primary,   0),
                new WeightedColor("#C70039", ColorRole.Secondary, 1),
                new WeightedColor("#FFC300", ColorRole.Accent,    2),
                new WeightedColor("#000000", ColorRole.Neutral,   3),
                new WeightedColor("#FFFFFF", null,                4),
            ],
            Logos: ["logo.png"],
            BackgroundTemplates:
            [
                new BrandBackgroundTemplateSnapshot(
                    "Story template",
                    "/storage/client-assets/org/client/story.png",
                    "story")
            ],
            Typography: ["Roboto"],
            VoiceAndTone: "professional",
            Notes: null),
        strategy: new StrategySnapshot(
            ContentPillars: ["education"],
            TargetAudience: "SMBs",
            PostingFrequency: "3x/week"),
        monthlyObjective: null);
}
