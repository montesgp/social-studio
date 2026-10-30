using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Infrastructure.Rendering;

namespace SocialStudio.Tests.Infrastructure.Rendering;

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
            FormatKey: "story",
            LayoutType: "story-story",
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
        """<div class="composition" data-format="story" style="width:1080px;height:1920px;background:#111111;"><div class="layer-background" data-layer="background" style="background:#111111;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;font-size:48px;">Test Headline</h1></div></div>""";

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
            FormatKey: "story",
            LayoutType: "story-story",
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

    // -----------------------------------------------------------------------
    // Task 1.5 — embedded @font-face injection (Content Studio emoji render spike, design #1168 §1
    // Decision A4). Unit-tested against the internal markup-composition seam — no real Chromium
    // required, since this only asserts what gets handed to SetContentAsync.
    // -----------------------------------------------------------------------

    [Fact]
    public void InjectFontFaceStyle_EmbedsFontFaceRuleForEmojiFamily()
    {
        var options = new RenderingOptions { Engine = RenderingEngine.Chromium };
        var renderer = new HtmlChromiumContentRenderer(
            browserPool: null,
            fallbackRenderer: new SpyFallbackRenderer(),
            options: options);

        var markup = renderer.InjectFontFaceStyle(BuildSampleHtml());

        Assert.Contains("@font-face", markup, StringComparison.Ordinal);
        Assert.Contains("Noto Color Emoji", markup, StringComparison.Ordinal);
        Assert.Contains("data:font/ttf;base64,", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectFontFaceStyle_AddsEmojiFamilyToDocumentFontStack()
    {
        var options = new RenderingOptions { Engine = RenderingEngine.Chromium };
        var renderer = new HtmlChromiumContentRenderer(
            browserPool: null,
            fallbackRenderer: new SpyFallbackRenderer(),
            options: options);

        var markup = renderer.InjectFontFaceStyle(BuildSampleHtml());

        // Emoji family must appear in a font-family declaration that applies document-wide, so any
        // slide-text run using it resolves without depending on the host having it installed.
        Assert.Matches(@"font-family\s*:[^;]*Noto Color Emoji", markup);
    }

    [Fact]
    public void InjectFontFaceStyle_PreservesOriginalMarkup()
    {
        var options = new RenderingOptions { Engine = RenderingEngine.Chromium };
        var renderer = new HtmlChromiumContentRenderer(
            browserPool: null,
            fallbackRenderer: new SpyFallbackRenderer(),
            options: options);
        var original = BuildSampleHtml();

        var markup = renderer.InjectFontFaceStyle(original);

        Assert.Contains(original, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectFontFaceStyle_CachesBuiltStyleAcrossCalls()
    {
        var options = new RenderingOptions { Engine = RenderingEngine.Chromium };
        var renderer = new HtmlChromiumContentRenderer(
            browserPool: null,
            fallbackRenderer: new SpyFallbackRenderer(),
            options: options);

        var first = renderer.InjectFontFaceStyle(BuildSampleHtml());
        var second = renderer.InjectFontFaceStyle(BuildSampleHtml());

        // Same style-block prefix both times — proves the (potentially expensive, base64-of-10MB)
        // style block is built once and cached, not rebuilt per render (Decision A4).
        var firstStyleBlock = first[..first.IndexOf("</style>", StringComparison.Ordinal)];
        var secondStyleBlock = second[..second.IndexOf("</style>", StringComparison.Ordinal)];
        Assert.Equal(firstStyleBlock, secondStyleBlock);
    }

    // -----------------------------------------------------------------------
    // client-style-fidelity D2 — catalog families named by the markup are embedded with their real
    // weight range, so Chromium never falls back to a host font or synthesises bold.
    // -----------------------------------------------------------------------

    [Fact]
    public void InjectFontFaceStyle_EmbedsReferencedCatalogFamilyWithItsWeightRange()
    {
        var renderer = NewRenderer();
        const string markup =
            "<h1 style=\"font-family:'Oswald',system-ui,sans-serif;font-weight:500\">Hola</h1>";

        var injected = renderer.InjectFontFaceStyle(markup);

        Assert.Contains("@font-face{font-family:'Oswald'", injected, StringComparison.Ordinal);
        Assert.Contains("font-weight:200 700;", injected, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectFontFaceStyle_DecodesHtmlEncodedFamilyNames()
    {
        var renderer = NewRenderer();
        const string markup = "<p style=\"font-family:&#39;League Gothic&#39;,sans-serif\">x</p>";

        var injected = renderer.InjectFontFaceStyle(markup);

        Assert.Contains("@font-face{font-family:'League Gothic'", injected, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectFontFaceStyle_DoesNotEmbedUnreferencedOrLegacyFamilies()
    {
        var renderer = NewRenderer();
        const string markup = "<p style=\"font-family:'Liberation Sans',system-ui,sans-serif\">x</p>";

        var injected = renderer.InjectFontFaceStyle(markup);

        // Cold-start markup keeps today's output: only the emoji face is declared.
        Assert.DoesNotContain("font-family:'Liberation Sans';src", injected, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family:'Oswald'", injected, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencedCatalogFamilies_ListsEachFamilyOnceInFirstSeenOrder()
    {
        const string markup =
            "<h1 style=\"font-family:'Anton',sans-serif\">a</h1>" +
            "<p style=\"font-family:Poppins, system-ui\">b</p>" +
            "<p style=\"font-family:'Anton'\">c</p>";

        var families = HtmlChromiumContentRenderer.ReferencedCatalogFamilies(markup);

        Assert.Equal(["Anton", "Poppins"], families.Select(f => f.Name));
    }

    private static HtmlChromiumContentRenderer NewRenderer() =>
        new(
            browserPool: null,
            fallbackRenderer: new SpyFallbackRenderer(),
            options: new RenderingOptions { Engine = RenderingEngine.Chromium });
}

// -----------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------

file sealed class SpyFallbackRenderer : SocialStudio.Application.Abstractions.Social.ContentGeneration.IContentRenderer
{
    public bool WasCalled { get; private set; }

    public Task<SocialStudio.Domain.Common.Result<SocialStudio.Domain.Social.ContentGeneration.RenderedAsset>> RenderAsync(
        SocialStudio.Domain.Social.ContentGeneration.VisualCompositionSpec spec,
        SocialStudio.Application.Abstractions.Social.ContentGeneration.RenderOptions opts,
        CancellationToken ct)
    {
        WasCalled = true;
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-png");
        var asset = SocialStudio.Domain.Social.ContentGeneration.RenderedAsset.Create(
            spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(SocialStudio.Domain.Common.Result.Success(asset));
    }
}
