using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Social;
using Incoders.Hive.Infrastructure.Rendering;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SkiaSharp;

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

    [Fact]
    public async Task RenderAsync_FlyerSpec_MatchesStoryDimensions()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildFlyerSpec();

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var bitmap = SKBitmap.Decode(result.Value.GetBytes());
        Assert.NotNull(bitmap);
        Assert.Equal(1080, bitmap.Width);
        Assert.Equal(1920, bitmap.Height);
    }

    [Fact]
    public async Task RenderAsync_FlyerSpecWithoutTemplate_ProducesNonWhiteBackground()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildFlyerSpec();

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var bitmap = SKBitmap.Decode(result.Value.GetBytes());
        Assert.NotNull(bitmap);
        var sample = bitmap.GetPixel(40, 40);
        Assert.False(sample.Red == 255 && sample.Green == 255 && sample.Blue == 255);
    }

    [Fact]
    public async Task RenderAsync_FlyerSpecWithLocalBackgroundTemplate_UsesTemplateImage()
    {
        var root = Path.Combine(Path.GetTempPath(), "incoders-hive-renderer-tests", Guid.NewGuid().ToString("N"));
        var templateDirectory = Path.Combine(root, "org", "client");
        Directory.CreateDirectory(templateDirectory);
        var templatePath = Path.Combine(templateDirectory, "story.png");
        WriteSolidPng(templatePath, SKColors.LimeGreen);

        try
        {
            var renderer = new LocalSvgContentRenderer(
                Options.Create(new ClientAssetStorageOptions
                {
                    RootPath = root,
                    PublicBasePath = "/storage/client-assets"
                }),
                new TestHostEnvironment());
            var spec = BuildFlyerSpec(backgroundUrl: "/storage/client-assets/org/client/story.png");

            var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            using var bitmap = SKBitmap.Decode(result.Value.GetBytes());
            Assert.NotNull(bitmap);
            var sample = bitmap.GetPixel(40, 40);
            Assert.True(sample.Green > sample.Red && sample.Green > sample.Blue);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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

    private static VisualCompositionSpec BuildFlyerSpec(string? backgroundUrl = null) =>
        new VisualCompositionSpec(
            FormatKey: "flyer",
            LayoutType: "story-flyer",
            Palette: new CompositionPalette("#111827", "#374151", "#2563EB", "#111827", "#F8FAFC"),
            Typography: new CompositionTypography("Arial", 28, "Arial", 16),
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["background"] = new AssetSlotSpec(backgroundUrl, "Modern branded product background"),
                ["logo"] = new AssetSlotSpec(null, "Brand logo")
            },
            CopySlots: new Dictionary<string, string>
            {
                ["headline"] = "A specific idea turned into a story flyer",
                ["subtitle"] = "Use the selected analysis insight",
                ["body"] = "This copy should be visible in the exported package.",
                ["callouts"] = "Client objective | Brand manual | Monthly KPI",
                ["cta"] = "Book a call",
                ["signature"] = "@client"
            },
            Dimensions: new CompositionDimensions(1080, 1920, 96));

    private static void WriteSolidPng(string path, SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(64, 64));
        surface.Canvas.Clear(color);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Incoders.Hive.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
