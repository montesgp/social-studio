using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Rendering;

namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// B3 WU-1 TDD tests for slot-geometry capture in <see cref="HtmlChromiumContentRenderer"/>.
///
/// <para>
/// D3 design decision: <c>getBoundingClientRect</c> geometry for each <c>[data-slot]</c> element
/// is captured during the existing Chromium render pass via <c>RenderWithGeometryAsync</c>.
/// No second browser pass is launched.
/// </para>
///
/// Browser-gated tests are skipped gracefully when Chromium is not installed,
/// following the same pattern as <see cref="HtmlChromiumContentRendererTests"/>.
///
/// Unit tests (mock pool seam) run without Chromium.
/// </summary>
public sealed class GeometryCaptureTests
{
    // PNG magic bytes
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static VisualCompositionSpec BuildHtmlSpecWithSlots(string? markup = null) =>
        new VisualCompositionSpec(
            FormatKey: "story",
            LayoutType: "story-flyer",
            Palette: new CompositionPalette("#111111", "#222222", "#3B82F6", "#FFFFFF", "#F9FAFB"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
            Dimensions: new CompositionDimensions(540, 960, 96))
        with
        {
            OutputKind = CompositionOutputKind.Html,
            DesignerMarkup = markup ?? BuildMarkupWithTwoSlots()
        };

    /// <summary>
    /// HTML with two [data-slot] elements: "headline" and "cta".
    /// Uses absolute positioning so geometry is deterministic.
    /// </summary>
    private static string BuildMarkupWithTwoSlots() => """
        <div style="position:relative;width:540px;height:960px;background:#111;">
          <div class="layer-background" data-layer="background" style="background:#111;position:absolute;inset:0;"></div>
          <div class="layer-content" data-layer="content">
            <h1 data-slot="headline"
                style="position:absolute;left:20px;top:100px;width:500px;height:60px;
                       font-family:Inter,sans-serif;font-size:32px;color:#FFFFFF;text-align:center;">
              Test Headline
            </h1>
            <p data-slot="cta"
               style="position:absolute;left:20px;top:200px;width:300px;height:40px;
                      font-family:Arial,sans-serif;font-size:16px;color:#FFFF00;text-align:left;">
              Learn More
            </p>
          </div>
        </div>
        """;

    // -----------------------------------------------------------------------
    // Unit tests: no Chromium required — verify interface contract
    // -----------------------------------------------------------------------

    /// <summary>
    /// HtmlChromiumContentRenderer MUST implement IContentRendererWithGeometry.
    /// This is a contract assertion — fails if the interface is not implemented.
    /// </summary>
    [Fact]
    public void HtmlChromiumContentRenderer_ImplementsIContentRendererWithGeometry()
    {
        var fallback = new SpyFallbackRenderer();
        var options = new RenderingOptions { Engine = RenderingEngine.Chromium };
        IContentRenderer renderer = new HtmlChromiumContentRenderer(null, fallback, options);

        Assert.IsAssignableFrom<IContentRendererWithGeometry>(renderer);
    }

    /// <summary>
    /// When the Skia engine is configured, RenderWithGeometryAsync MUST return success
    /// with an empty SlotGeometry list (delegates to fallback, no geometry).
    /// </summary>
    [Fact]
    public async Task RenderWithGeometryAsync_SkiaEngine_ReturnsEmptyGeometry()
    {
        var fallback = new SpyFallbackRenderer();
        var renderer = new HtmlChromiumContentRenderer(
            browserPool: null,
            fallbackRenderer: fallback,
            options: new RenderingOptions { Engine = RenderingEngine.Skia });

        var spec = BuildHtmlSpecWithSlots();
        var result = await renderer.RenderWithGeometryAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(fallback.WasCalled, "Skia engine must delegate to fallback.");
        Assert.Empty(result.Value.SlotGeometry);
    }

    /// <summary>
    /// For non-HTML specs (Raster/Text), RenderWithGeometryAsync MUST return success
    /// with an empty SlotGeometry list (delegates to fallback, no geometry).
    /// </summary>
    [Fact]
    public async Task RenderWithGeometryAsync_NonHtmlSpec_ReturnsEmptyGeometry()
    {
        var fallback = new SpyFallbackRenderer();
        var renderer = new HtmlChromiumContentRenderer(
            browserPool: null,
            fallbackRenderer: fallback,
            options: new RenderingOptions { Engine = RenderingEngine.Chromium });

        var rasterSpec = new VisualCompositionSpec(
            FormatKey: "story",
            LayoutType: "story-flyer",
            Palette: new CompositionPalette("#111", "#222", "#333", "#FFF", "#EEE"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string>(),
            Dimensions: new CompositionDimensions(1080, 1920, 96))
        with { OutputKind = CompositionOutputKind.Raster };

        var result = await renderer.RenderWithGeometryAsync(rasterSpec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(fallback.WasCalled, "Non-Html spec must delegate to fallback.");
        Assert.Empty(result.Value.SlotGeometry);
    }

    // -----------------------------------------------------------------------
    // Browser-gated integration tests — skipped when Chromium absent
    // -----------------------------------------------------------------------

    /// <summary>
    /// INTEGRATION (browser-gated, D3): RenderWithGeometryAsync on a valid HTML spec with
    /// [data-slot] elements MUST return a non-empty SlotGeometry list. Each entry must have
    /// a non-empty Slot name and a positive Width/Height.
    /// </summary>
    [Fact]
    public async Task RenderWithGeometryAsync_HtmlSpecWithSlots_ReturnsMeasuredGeometry()
    {
        if (!ChromiumAvailabilityCheck.IsInstalled())
        {
            return; // Graceful skip when Chromium not installed
        }

        var options = new RenderingOptions { Engine = RenderingEngine.Chromium, DeviceScaleFactor = 2 };
        await using var pool = new ChromiumBrowserPool(options);
        await pool.InitializeAsync(CancellationToken.None);

        var renderer = new HtmlChromiumContentRenderer(pool, new SpyFallbackRenderer(), options);
        var spec = BuildHtmlSpecWithSlots();

        var result = await renderer.RenderWithGeometryAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess, $"Render failed: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");

        // D3: geometry array must be non-empty — we have two [data-slot] elements
        Assert.NotEmpty(result.Value.SlotGeometry);

        // Each entry must have a non-empty slot name
        Assert.All(result.Value.SlotGeometry, g => Assert.NotEmpty(g.Slot));

        // Each entry must have positive dimensions (element is visible)
        Assert.All(result.Value.SlotGeometry, g =>
        {
            Assert.True(g.Width > 0, $"Slot '{g.Slot}' must have positive Width.");
            Assert.True(g.Height > 0, $"Slot '{g.Slot}' must have positive Height.");
        });

        // Verify both expected slots are present
        var slots = result.Value.SlotGeometry.Select(g => g.Slot).ToHashSet();
        Assert.Contains("headline", slots);
        Assert.Contains("cta", slots);

        // PNG asset is also present
        Assert.Equal("image/png", result.Value.Asset.Mime);
        Assert.True(result.Value.Asset.SizeBytes > 0);
        Assert.Equal(PngSignature, result.Value.Asset.GetBytes()[..8]);
    }

    /// <summary>
    /// INTEGRATION (browser-gated): When markup has no [data-slot] elements,
    /// RenderWithGeometryAsync MUST succeed with an empty SlotGeometry list
    /// (render still completes, geometry is just empty — not a failure).
    /// </summary>
    [Fact]
    public async Task RenderWithGeometryAsync_HtmlSpecNoSlots_ReturnsEmptyGeometryAndSucceeds()
    {
        if (!ChromiumAvailabilityCheck.IsInstalled())
        {
            return;
        }

        var options = new RenderingOptions { Engine = RenderingEngine.Chromium, DeviceScaleFactor = 2 };
        await using var pool = new ChromiumBrowserPool(options);
        await pool.InitializeAsync(CancellationToken.None);

        var renderer = new HtmlChromiumContentRenderer(pool, new SpyFallbackRenderer(), options);

        var noSlotMarkup = """
            <div style="width:540px;height:960px;background:#000;">
              <p style="color:#FFF;">No data-slot elements here.</p>
            </div>
            """;
        var spec = BuildHtmlSpecWithSlots(noSlotMarkup);

        var result = await renderer.RenderWithGeometryAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess, $"Render should succeed even without slots: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");
        Assert.Empty(result.Value.SlotGeometry);
        Assert.True(result.Value.Asset.SizeBytes > 0);
    }

    /// <summary>
    /// INTEGRATION (browser-gated): RenderAsync (base interface) must still work correctly
    /// after the renderer implements IContentRendererWithGeometry. Backward-compat regression guard.
    /// </summary>
    [Fact]
    public async Task RenderAsync_AfterGeometryRendererRefactor_StillReturnsPng()
    {
        if (!ChromiumAvailabilityCheck.IsInstalled())
        {
            return;
        }

        var options = new RenderingOptions { Engine = RenderingEngine.Chromium, DeviceScaleFactor = 2 };
        await using var pool = new ChromiumBrowserPool(options);
        await pool.InitializeAsync(CancellationToken.None);

        var renderer = new HtmlChromiumContentRenderer(pool, new SpyFallbackRenderer(), options);
        var spec = BuildHtmlSpecWithSlots();

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess, $"Base RenderAsync must still work: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");
        Assert.Equal("image/png", result.Value.Mime);
        Assert.Equal(PngSignature, result.Value.GetBytes()[..8]);
    }
}

// -----------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------

file sealed class SpyFallbackRenderer : IContentRenderer
{
    public bool WasCalled { get; private set; }

    public Task<Result<RenderedAsset>> RenderAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        WasCalled = true;
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-png");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Result.Success(asset));
    }
}
