using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Flyer;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

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
    // Flyer + brand → flyer-specific layout
    // -------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_Flyer_FullBrand_ProducesFlyerLayout()
    {
        var service = CreateService();
        var brand = BuildFullBrand();
        var payload = new FlyerContent(
            "Grand Opening",
            "This week only",
            "A flyer based on the selected idea.",
            ["Brand-safe", "Story-ready"],
            "Book now",
            "Product-led branded background",
            "@client",
            null);

        var spec = await service.ComposeAsync("flyer", payload, brand, CancellationToken.None);

        Assert.Equal("flyer", spec.FormatKey);
        Assert.Equal(1080, spec.Dimensions.Width);
        Assert.Equal(1920, spec.Dimensions.Height);
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
    // Helpers
    // -------------------------------------------------------------------

    private static VisualCompositionService CreateService() => new();

    private static BrandSnapshot BuildFullBrand() => BrandSnapshot.FromBrandData(
        brand: new BrandManualSnapshot(
            ColorPalette: ["#FF5733", "#C70039", "#FFC300", "#000000", "#FFFFFF"],
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
            ToneKeywords: ["warm"],
            TargetAudience: "SMBs",
            PostingFrequency: "3x/week"),
        monthlyObjective: null);
}
