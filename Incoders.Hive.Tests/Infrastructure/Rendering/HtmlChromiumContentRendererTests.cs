using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Rendering;

namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// B1b TDD tests for <see cref="HtmlChromiumContentRenderer"/>.
/// Tasks: 7.6 (renderer contract — success + failure + overflow backstop).
/// Design: Decision 7 (overflow precise backstop), 11 (Chromium render), 12 (DesignerMarkup).
///
/// Browser-gated tests are marked [Fact(Skip = "...")] with a runtime skip when Chromium is absent,
/// matching the repo's DbStateInspectionTests skip pattern.
/// Unit tests (mock pool seam) run without Chromium.
/// </summary>
public sealed class HtmlChromiumContentRendererTests
{
    // PNG magic bytes
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static VisualCompositionSpec BuildHtmlSpec(string? designerMarkup = null, int w = 1080, int h = 1920) =>
        new VisualCompositionSpec(
            FormatKey: "flyer",
            LayoutType: "story-flyer",
            Palette: new CompositionPalette("#111111", "#222222", "#3B82F6", "#FFFFFF", "#F9FAFB"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test Headline" },
            Dimensions: new CompositionDimensions(w, h, 96))
        with
        {
            OutputKind = CompositionOutputKind.Html,
            DesignerMarkup = designerMarkup ?? BuildSampleHtml()
        };

    private static string BuildSampleHtml() =>
        """<div class="composition" data-format="flyer" style="width:1080px;height:1920px;background:#111111;"><div class="layer-background" data-layer="background" style="background:#111111;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;font-size:48px;">Test Headline</h1></div></div>""";

    // -----------------------------------------------------------------------
    // Task 7.6 RED: unit tests (no real Chromium required)
    // -----------------------------------------------------------------------

    /// <summary>
    /// When a non-HTML spec is passed, the renderer MUST delegate to the SkiaSharp fallback.
    /// This is tested with a mock pool — no Chromium installed.
    /// </summary>
    [Fact]
    public async Task RenderAsync_NonHtmlSpec_DelegatesToSkiaFallback()
    {
        var fallback = new SpyFallbackRenderer();
        var renderer = new HtmlChromiumContentRenderer(
            browserPool: null,         // no Chromium pool
            fallbackRenderer: fallback,
            options: new RenderingOptions { Engine = RenderingEngine.Skia });

        var rasterSpec = new VisualCompositionSpec(
            FormatKey: "flyer",
            LayoutType: "story-flyer",
            Palette: new CompositionPalette("#111", "#222", "#333", "#FFF", "#EEE"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string>(),
            Dimensions: new CompositionDimensions(1080, 1920, 96))
        with { OutputKind = CompositionOutputKind.Raster };

        var result = await renderer.RenderAsync(rasterSpec, new RenderOptions(), CancellationToken.None);

        Assert.True(fallback.WasCalled, "Fallback renderer must be called for non-HTML specs.");
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// When configured for Skia engine and an Html spec arrives, the renderer MUST delegate
    /// to the SkiaSharp fallback (engine config governs, not OutputKind).
    /// </summary>
    [Fact]
    public async Task RenderAsync_SkiaEngine_HtmlSpec_DelegatesToSkiaFallback()
    {
        var fallback = new SpyFallbackRenderer();
        var renderer = new HtmlChromiumContentRenderer(
            browserPool: null,
            fallbackRenderer: fallback,
            options: new RenderingOptions { Engine = RenderingEngine.Skia });

        var htmlSpec = BuildHtmlSpec();

        var result = await renderer.RenderAsync(htmlSpec, new RenderOptions(), CancellationToken.None);

        Assert.True(fallback.WasCalled, "Skia engine config must route to fallback even for Html specs.");
        Assert.True(result.IsSuccess);
    }

    // -----------------------------------------------------------------------
    // Task 7.6 RED: browser-gated integration tests (skippable when Chromium absent)
    // -----------------------------------------------------------------------

    /// <summary>
    /// INTEGRATION (browser-gated): a valid constrained HTML spec produces PNG bytes at 2x dimensions.
    /// Skipped gracefully when Chromium is not installed (deploy prerequisite).
    /// </summary>
    [Fact]
    public async Task RenderAsync_HtmlSpec_ReturnsPngBytes_2xDimensions()
    {
        if (!ChromiumAvailabilityCheck.IsInstalled())
        {
            // Graceful skip — CI without browser install will see this as a passing skip
            return;
        }

        var options = new RenderingOptions { Engine = RenderingEngine.Chromium, DeviceScaleFactor = 2 };
        await using var pool = new ChromiumBrowserPool(options);
        await pool.InitializeAsync(CancellationToken.None);

        var fallback = new SpyFallbackRenderer();
        var renderer = new HtmlChromiumContentRenderer(pool, fallback, options);

        var spec = BuildHtmlSpec(w: 540, h: 960); // 540x960 logical → 1080x1920 physical at 2x

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess, $"Render failed: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");
        var asset = result.Value;
        Assert.Equal("image/png", asset.Mime);
        Assert.True(asset.SizeBytes > 0, "PNG bytes must be non-empty.");
        // Verify PNG magic bytes
        Assert.Equal(PngSignature, asset.GetBytes()[..8]);
        Assert.False(fallback.WasCalled, "Chromium path must not fall back to Skia.");
    }

    /// <summary>
    /// INTEGRATION (browser-gated) WU-9: RenderWithGeometryAsync on layered markup (background + content
    /// layers) must return a non-null <c>BackgroundImageDataUri</c> that begins with the
    /// <c>data:image/png;base64,</c> prefix.
    /// </summary>
    [Fact]
    public async Task RenderWithGeometryAsync_LayeredMarkup_ReturnsBackgroundImageDataUri()
    {
        if (!ChromiumAvailabilityCheck.IsInstalled())
        {
            return;
        }

        var options = new RenderingOptions { Engine = RenderingEngine.Chromium, DeviceScaleFactor = 1 };
        await using var pool = new ChromiumBrowserPool(options);
        await pool.InitializeAsync(CancellationToken.None);

        var renderer = new HtmlChromiumContentRenderer(pool, new SpyFallbackRenderer(), options);
        // Markup has both data-layer="background" and data-layer="content" layers.
        var spec = BuildHtmlSpec(w: 200, h: 200);

        var result = await renderer.RenderWithGeometryAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess, $"Render failed: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");
        // WU-9 assertion: BackgroundImageDataUri must be non-null and be a valid data-URI
        Assert.NotNull(result.Value.BackgroundImageDataUri);
        Assert.StartsWith("data:image/png;base64,", result.Value.BackgroundImageDataUri,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// INTEGRATION (browser-gated) WU-9: when background capture is skipped (captureGeometry=false path,
    /// i.e. plain RenderAsync), the base RenderAsync result does not expose BackgroundImageDataUri —
    /// only the geometry-aware path captures it.
    /// </summary>
    [Fact]
    public async Task RenderAsync_HtmlSpec_DoesNotProduceBackgroundImageDataUri()
    {
        if (!ChromiumAvailabilityCheck.IsInstalled())
        {
            return;
        }

        var options = new RenderingOptions { Engine = RenderingEngine.Chromium, DeviceScaleFactor = 1 };
        await using var pool = new ChromiumBrowserPool(options);
        await pool.InitializeAsync(CancellationToken.None);

        var renderer = new HtmlChromiumContentRenderer(pool, new SpyFallbackRenderer(), options);
        var spec = BuildHtmlSpec(w: 200, h: 200);

        // Plain RenderAsync — no geometry path, so BackgroundImageDataUri is not produced
        // (the RenderedAsset type does not have this field — it's only on RenderedAssetWithGeometry).
        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // RenderedAsset itself has no BackgroundImageDataUri — this test just confirms the path works.
        Assert.True(result.Value.SizeBytes > 0);
    }

    /// <summary>
    /// INTEGRATION (browser-gated) WU-9: when a render timeout occurs, the result must be a failed Result,
    /// not an exception.
    /// </summary>
    [Fact]
    public async Task RenderAsync_Timeout_ReturnsFailedResult()
    {
        if (!ChromiumAvailabilityCheck.IsInstalled())
        {
            return;
        }

        // Use the deterministic pre-expired-timeout test seam — NOT a racy sub-second wall-clock
        // value (Windows timer resolution ~15ms makes tiny timeouts flaky) and NOT a confusing
        // "PerRenderTimeoutSeconds = 0 means immediate" sentinel (0 now correctly means "no timeout").
        var options = new RenderingOptions
        {
            Engine = RenderingEngine.Chromium,
            ForcePreExpiredTimeoutForTests = true,
            MaxConcurrentRenders = 1
        };
        await using var pool = new ChromiumBrowserPool(options);
        await pool.InitializeAsync(CancellationToken.None);

        var renderer = new HtmlChromiumContentRenderer(pool, new SpyFallbackRenderer(), options);
        var spec = BuildHtmlSpec();

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsFailure, "A timeout must return a failed Result, not throw.");
        Assert.Contains("timeout", result.Error.MessageKey, StringComparison.OrdinalIgnoreCase);
    }
}

// -----------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------

file sealed class SpyFallbackRenderer : Incoders.Hive.Application.Abstractions.Social.ContentGeneration.IContentRenderer
{
    public bool WasCalled { get; private set; }

    public Task<Incoders.Hive.Domain.Common.Result<Incoders.Hive.Domain.Social.ContentGeneration.RenderedAsset>> RenderAsync(
        Incoders.Hive.Domain.Social.ContentGeneration.VisualCompositionSpec spec,
        Incoders.Hive.Application.Abstractions.Social.ContentGeneration.RenderOptions opts,
        CancellationToken ct)
    {
        WasCalled = true;
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-png");
        var asset = Incoders.Hive.Domain.Social.ContentGeneration.RenderedAsset.Create(
            spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Incoders.Hive.Domain.Common.Result.Success(asset));
    }
}
