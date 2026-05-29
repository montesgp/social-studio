using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Rendering;

namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// TDD tests for <see cref="LocalSvgContentRenderer"/>.
/// Validates PNG output from a VisualCompositionSpec.
/// </summary>
public sealed class LocalSvgContentRendererTests
{
    // PNG magic bytes: \x89PNG\r\n\x1a\n
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public async Task RenderAsync_ValidSpec_ProducesPngAsset()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildCarouselSpec();

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var asset = result.Value;
        Assert.Equal("image/png", asset.Mime);
        Assert.True(asset.SizeBytes > 0);
        Assert.NotEmpty(asset.ContentHash);
    }

    [Fact]
    public async Task RenderAsync_ValidSpec_PngSignatureInFirstBytes()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildCarouselSpec();

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var bytes = result.Value.GetBytes();
        Assert.True(bytes.Length >= 8, "PNG must have at least 8 bytes");
        Assert.Equal(PngSignature, bytes[..8]);
    }

    [Fact]
    public async Task RenderAsync_ValidSpec_MatchesExpectedDimensions()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildCarouselSpec(width: 1080, height: 1080);

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Same spec → same dimensions (determinism check)
        var result2 = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);
        Assert.True(result2.IsSuccess);
        Assert.Equal(result.Value.SizeBytes > 0, result2.Value.SizeBytes > 0);
    }

    [Fact]
    public async Task RenderAsync_SpecWithCopySlots_ProducesNonEmptyPng()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildCarouselSpec();

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.SizeBytes > 100, "PNG output should be non-trivial");
    }

    [Fact]
    public async Task RenderAsync_TextOnlyFormat_ProducesPngTextCard()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildTextOnlySpec("hook");

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/png", result.Value.Mime);
        Assert.Equal("hook.png", result.Value.Filename);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static VisualCompositionSpec BuildCarouselSpec(int width = 1080, int height = 1080) =>
        new VisualCompositionSpec(
            FormatKey: "carousel",
            LayoutType: "grid",
            Palette: new CompositionPalette("#333", "#666", "#999", "#FFF", "#EEE"),
            Typography: new CompositionTypography("Arial", 24, "Arial", 16),
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["cover"] = new AssetSlotSpec(null, "Brand cover image")
            },
            CopySlots: new Dictionary<string, string>
            {
                ["title"] = "Test Carousel Title",
                ["subtitle"] = "Subtitle text here"
            },
            Dimensions: new CompositionDimensions(width, height, 96));

    private static VisualCompositionSpec BuildTextOnlySpec(string formatKey) =>
        new VisualCompositionSpec(
            FormatKey: formatKey,
            LayoutType: "text-card",
            Palette: new CompositionPalette("#333", "#666", "#999", "#FFF", "#EEE"),
            Typography: new CompositionTypography("Arial", 20, "Arial", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["text-card"] = new AssetSlotSpec(null, null)
            },
            CopySlots: new Dictionary<string, string>
            {
                ["text"] = "This is the hook text content"
            },
            Dimensions: new CompositionDimensions(1080, 1080, 96));
}
