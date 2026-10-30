using System.Text;
using System.Text.RegularExpressions;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Infrastructure.Rendering.Fonts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace SocialStudio.Infrastructure.Rendering;

/// <summary>
/// <see cref="IContentRenderer"/> adapter that renders HTML/CSS specifications authored by
/// <c>IVisualDesigner</c> via headless Chromium (Playwright .NET), producing retina-quality PNGs.
///
/// <para>
/// When <see cref="RenderingOptions.Engine"/> is <see cref="RenderingEngine.Skia"/>, or when the
/// spec's <see cref="VisualCompositionSpec.OutputKind"/> is NOT <see cref="CompositionOutputKind.Html"/>,
/// all rendering is delegated to the configured <c>fallbackRenderer</c>
/// (<see cref="LocalSvgContentRenderer"/>).
/// </para>
///
/// <para>
/// Design: Decision 11 (pooled Chromium, DeviceScaleFactor=2, per-render timeout),
/// Decision 12 (CompositionOutputKind.Html + DesignerMarkup path).
/// </para>
///
/// <para>
/// Deploy prerequisite: <c>playwright install chromium</c> must be run before this renderer
/// is used. See DEPLOY-NOTES.md. Test environments can skip browser-gated tests via
/// <see cref="ChromiumAvailabilityCheck.IsInstalled()"/>.
/// </para>
/// </summary>
/// <summary>
/// JavaScript snippet evaluated by Playwright to capture per-[data-slot] bounding rects
/// and computed text styles during the existing render pass (D3).
/// Returns a JSON array of slot geometry objects.
/// </summary>
file static class SlotGeometryScript
{
    /// <summary>
    /// $$eval selector: matches every element with a [data-slot] attribute.
    /// Extracts getBoundingClientRect() + getComputedStyle() for each element.
    /// Returns an array of objects with slot, x, y, width, height, fontFamily,
    /// fontSize, color, textAlign fields.
    /// </summary>
    public const string Selector = "[data-slot]";

    public const string ExtractExpression = @"
        elements => elements.map(el => {
            const r = el.getBoundingClientRect();
            const s = window.getComputedStyle(el);
            const ff = (s.fontFamily || '').split(',')[0].replace(/[""']/g, '').trim();
            const fsPx = parseFloat(s.fontSize) || 0;
            const lhPx = parseFloat(s.lineHeight);    // 'normal' -> NaN
            const lsPx = parseFloat(s.letterSpacing); // 'normal' -> NaN

            // content-layout-and-image-fidelity WU-6 (CLIF-CS-01): el's own box carries a
            // CSS min-height literal, which reports a padded box (not the glyph ink) whenever the
            // rendered text is shorter than that min-height. A Range over the element's contents
            // measures the actual rendered line box(es) instead. When the content already exceeds
            // min-height (a long, wrapping headline), the element's own box already equals the
            // content height, so this is a no-op there — only the short-text/padded case tightens.
            let box = r;
            try {
                const range = document.createRange();
                range.selectNodeContents(el);
                const textRect = range.getBoundingClientRect();
                if (textRect.width > 0 && textRect.height > 0) {
                    box = textRect;
                }
            } catch (e) {
                // Range measurement unavailable (e.g. empty/replaced content) — keep the CSS box.
            }

            return {
                slot: el.getAttribute('data-slot') || '',
                x: box.x,
                y: box.y,
                width: box.width,
                height: box.height,
                fontFamily: ff || null,
                fontSizePx: fsPx,
                color: s.color || null,
                textAlign: s.textAlign || null,
                fontWeight: s.fontWeight || null,
                fontStyle: s.fontStyle || null,
                lineHeightPx: isNaN(lhPx) ? 0 : lhPx,
                letterSpacingPx: isNaN(lsPx) ? 0 : lsPx,
                textTransform: s.textTransform || null
            };
        })
    ";
}

public sealed class HtmlChromiumContentRenderer : IContentRendererWithGeometry
{
    /// <summary>Family name of the bundled emoji font — see Decision A1 (design #1168 §1).</summary>
    private const string EmojiFamily = "Noto Color Emoji";

    private static readonly IFontProvider DefaultFontProvider = new EmbeddedFontProvider();

    private readonly ChromiumBrowserPool? _browserPool;
    private readonly IContentRenderer _fallbackRenderer;
    private readonly RenderingOptions _options;
    private readonly ILogger<HtmlChromiumContentRenderer> _logger;
    private readonly IFontProvider _fonts;

    /// <summary>
    /// Built once, reused for every render (Decision A4 — a 10MB base64 emoji font must not be
    /// rebuilt per request). <c>null</c> means "not built yet"; an empty string means "no embedded
    /// fonts available to inject" (still cached, so we don't retry every render).
    /// </summary>
    private string? _cachedFontFaceStyle;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _catalogFaceRules =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fontFaceStyleLock = new();

    /// <summary>
    /// Initializes the renderer.
    /// </summary>
    /// <param name="browserPool">
    /// Pooled Chromium browser. Pass <c>null</c> when <paramref name="options"/> selects Skia engine
    /// (pool is not required). When engine is Chromium and pool is null, HTML renders will fail.
    /// </param>
    /// <param name="fallbackRenderer">
    /// Fallback renderer — used for non-HTML specs, or for all specs when engine is Skia.
    /// </param>
    /// <param name="options">Rendering configuration (engine, timeout, scale factor).</param>
    /// <param name="logger">
    /// Logger for geometry capture warnings. Pass <c>null</c> in unit tests (defaults to NullLogger).
    /// </param>
    /// <param name="fonts">
    /// Embedded font source for the injected <c>@font-face</c> style block (Decision A4). Pass
    /// <c>null</c> to use the default <see cref="EmbeddedFontProvider"/>.
    /// </param>
    public HtmlChromiumContentRenderer(
        ChromiumBrowserPool? browserPool,
        IContentRenderer fallbackRenderer,
        RenderingOptions options,
        ILogger<HtmlChromiumContentRenderer>? logger = null,
        IFontProvider? fonts = null)
    {
        ArgumentNullException.ThrowIfNull(fallbackRenderer);
        ArgumentNullException.ThrowIfNull(options);

        _browserPool = browserPool;
        _fallbackRenderer = fallbackRenderer;
        _options = options;
        _logger = logger ?? NullLogger<HtmlChromiumContentRenderer>.Instance;
        _fonts = fonts ?? DefaultFontProvider;
    }

    /// <inheritdoc />
    public async Task<Result<RenderedAsset>> RenderAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        // Engine config governs: Skia always delegates to fallback (even for Html specs).
        if (_options.Engine == RenderingEngine.Skia)
        {
            return await _fallbackRenderer.RenderAsync(spec, opts, ct);
        }

        // Non-HTML output kinds: delegate to fallback.
        if (spec.OutputKind != CompositionOutputKind.Html)
        {
            return await _fallbackRenderer.RenderAsync(spec, opts, ct);
        }

        // Chromium engine + Html spec: render via Playwright (no geometry capture).
        var result = await RenderChromiumAsync(spec, opts, captureGeometry: false, ct);
        return result.IsSuccess
            ? Result.Success(result.Value.Asset)
            : Result.Failure<RenderedAsset>(result.Error);
    }

    /// <inheritdoc />
    public async Task<Result<RenderedAssetWithGeometry>> RenderWithGeometryAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        // Engine config governs: Skia always delegates to base RenderAsync (no geometry).
        if (_options.Engine == RenderingEngine.Skia)
        {
            var fallbackResult = await _fallbackRenderer.RenderAsync(spec, opts, ct);
            return fallbackResult.IsSuccess
                ? Result.Success(new RenderedAssetWithGeometry(fallbackResult.Value, []))
                : Result.Failure<RenderedAssetWithGeometry>(fallbackResult.Error);
        }

        // Non-HTML output kinds: delegate to fallback (no geometry).
        if (spec.OutputKind != CompositionOutputKind.Html)
        {
            var fallbackResult = await _fallbackRenderer.RenderAsync(spec, opts, ct);
            return fallbackResult.IsSuccess
                ? Result.Success(new RenderedAssetWithGeometry(fallbackResult.Value, []))
                : Result.Failure<RenderedAssetWithGeometry>(fallbackResult.Error);
        }

        // Chromium engine + Html spec: render with geometry capture.
        return await RenderChromiumAsync(spec, opts, captureGeometry: true, ct);
    }

    private async Task<Result<RenderedAssetWithGeometry>> RenderChromiumAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        bool captureGeometry,
        CancellationToken ct)
    {
        if (_browserPool is null)
        {
            return Result.Failure<RenderedAssetWithGeometry>(
                RenderErrors.RenderFailed("ChromiumBrowserPool is null — engine is Chromium but pool was not injected."));
        }

        var markup = spec.DesignerMarkup;
        if (string.IsNullOrWhiteSpace(markup))
        {
            return Result.Failure<RenderedAssetWithGeometry>(
                RenderErrors.RenderFailed("DesignerMarkup is null or empty — cannot render Html spec without markup."));
        }

        // Resolve the effective per-render wall-clock timeout.
        //   - ForcePreExpiredTimeoutForTests: deterministic pre-cancelled CTS (test seam only).
        //   - PerRenderTimeoutSeconds (positive): a real second-granularity deadline.
        //   - else (0 / non-positive): NO timeout — the render is bounded only by the caller's token.
        using var timeoutCts = CreateTimeoutCts(_options);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var renderCt = linkedCts.Token;

        try
        {
            await using var lease = await _browserPool.AcquirePageAsync(
                spec.Dimensions.Width, spec.Dimensions.Height, renderCt);

            var page = lease.Page;

            // Set HTML content and wait for networkidle to allow inline fonts/styles to apply.
            // Decision A4: prepend a cached @font-face style block so emoji (and any other bundled
            // font) renders correctly without depending on the host having it installed.
            var markupWithFonts = InjectFontFaceStyle(markup);
            await page.SetContentAsync(markupWithFonts, new PageSetContentOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // WU-1 D3: capture per-[data-slot] bounding rects during the existing page lifetime.
            // This piggybacks the render that already runs — no second Chromium pass.
            IReadOnlyList<SlotGeometryEntry> slotGeometry = [];
            if (captureGeometry)
            {
                try
                {
                    slotGeometry = await CaptureSlotGeometryAsync(page, renderCt);
                }
                catch (Exception geoEx)
                {
                    // Geometry capture failure is non-fatal — the render proceeds with empty geometry.
                    // Log at Warning so production issues are visible but the asset is still delivered.
                    _logger.LogWarning(
                        geoEx,
                        "Slot geometry capture failed for format '{FormatKey}'; proceeding with empty geometry. {Message}",
                        spec.FormatKey,
                        geoEx.Message);
                }
            }

            // Capture the bounding box of the root composition element for overflow backstop.
            // If the rendered element overflows the declared canvas, we still capture the
            // viewport area only — preventing unbounded screenshots (Design Decision 7 precise backstop).
            var screenshotBytes = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Type = ScreenshotType.Png,
                Clip = new Clip
                {
                    X = 0,
                    Y = 0,
                    Width = spec.Dimensions.Width,
                    Height = spec.Dimensions.Height
                },
                FullPage = false
            });

            if (screenshotBytes is null || screenshotBytes.Length == 0)
            {
                return Result.Failure<RenderedAssetWithGeometry>(
                    RenderErrors.RenderFailed("Playwright screenshot returned empty bytes."));
            }

            var asset = RenderedAsset.Create(
                spec.FormatKey,
                "image/png",
                $"{spec.FormatKey}.png",
                screenshotBytes);

            // WU-9 D3: capture background-only PNG when geometry is requested (same page session).
            // Hide the content layer, take a second screenshot, then restore — no second full render.
            string? backgroundDataUri = null;
            if (captureGeometry)
            {
                backgroundDataUri = await CaptureBackgroundDataUriAsync(
                    page, spec, _logger, renderCt);
            }

            return Result.Success(new RenderedAssetWithGeometry(asset, slotGeometry, backgroundDataUri));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Result.Failure<RenderedAssetWithGeometry>(
                RenderErrors.RenderFailed(
                    $"Chromium render timeout after {DescribeTimeout(_options)} for format '{spec.FormatKey}'."));
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<RenderedAssetWithGeometry>(
                RenderErrors.RenderFailed("Chromium render cancelled by caller."));
        }
        catch (PlaywrightException ex)
        {
            return Result.Failure<RenderedAssetWithGeometry>(
                RenderErrors.RenderFailed($"Playwright error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Result.Failure<RenderedAssetWithGeometry>(
                RenderErrors.RenderFailed($"Unexpected render error: {ex.Message}"));
        }
    }

    /// <summary>
    /// Prepends a cached <c>&lt;style&gt;</c> block declaring <c>@font-face</c> rules for every
    /// embedded font (Decision A4, design #1168 §1) to <paramref name="markup"/>, and adds those
    /// families to a document-wide <c>font-family</c> stack so any text run — including one Chromium
    /// picks a font for on its own — can resolve the emoji family without requiring it on the host.
    /// </summary>
    /// <remarks>
    /// Internal (not private) so this can be unit-tested directly without a real Chromium instance —
    /// it only asserts what would be handed to <c>Page.SetContentAsync</c>.
    /// The browser parses a leading &lt;style&gt; tag into &lt;head&gt; per standard HTML5 foster-parenting
    /// rules, so this does not require the caller's markup to already have a &lt;head&gt; element.
    /// </remarks>
    internal string InjectFontFaceStyle(string markup)
    {
        var style = GetOrBuildFontFaceStyle() + BuildCatalogFontFaceStyle(markup);
        return string.IsNullOrEmpty(style) ? markup : style + markup;
    }

    /// <summary>
    /// client-style-fidelity D2: <c>@font-face</c> rules for every bundled client-typography family the
    /// markup names in a <c>font-family</c> declaration. Without them Chromium silently draws the
    /// host's fallback font, which is how a client's condensed headline rendered as a wide system
    /// sans. Only catalog families are injected; the legacy Liberation/DejaVu fallbacks keep today's
    /// behaviour so cold-start output does not change. Per-family CSS is cached because each family
    /// is base64 of a few hundred KB.
    /// </summary>
    private string BuildCatalogFontFaceStyle(string markup)
    {
        var families = ReferencedCatalogFamilies(markup);
        if (families.Count == 0)
        {
            return string.Empty;
        }

        var style = new StringBuilder("<style>");
        foreach (var family in families)
        {
            style.Append(_catalogFaceRules.GetOrAdd(family.Name, _ => BuildFamilyFaceRules(family.Name)));
        }

        return style.Append("</style>").ToString();
    }

    private string BuildFamilyFaceRules(string family)
    {
        var rules = new StringBuilder();
        foreach (var face in _fonts.ResolveFaces(family))
        {
            rules.Append("@font-face{font-family:'")
                .Append(family)
                .Append("';src:url(data:font/ttf;base64,")
                .Append(Convert.ToBase64String(face.Bytes))
                .Append(");font-weight:")
                .Append(face.MinWeight)
                .Append(' ')
                .Append(face.MaxWeight)
                .Append(";}");
        }

        return rules.ToString();
    }

    private static readonly Regex FontFamilyDeclaration = new(
        @"font-family\s*:\s*(?<value>[^;}""]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Catalog families named in any <c>font-family</c> declaration of <paramref name="markup"/>,
    /// in first-seen order. Values may be HTML-encoded inside a <c>style</c> attribute.
    /// </summary>
    internal static IReadOnlyList<BundledFontFamily> ReferencedCatalogFamilies(string markup)
    {
        var found = new List<BundledFontFamily>();
        // Decode first: an encoded quote (&#39;) carries a ';' that would otherwise end the match early.
        var decoded = System.Net.WebUtility.HtmlDecode(markup);
        foreach (Match match in FontFamilyDeclaration.Matches(decoded))
        {
            foreach (var raw in match.Groups["value"].Value.Split(','))
            {
                var family = BundledFontCatalog.Find(raw.Trim().Trim('\'', '"').Trim());
                if (family is not null && !found.Contains(family))
                {
                    found.Add(family);
                }
            }
        }

        return found;
    }

    private string GetOrBuildFontFaceStyle()
    {
        if (_cachedFontFaceStyle is not null)
        {
            return _cachedFontFaceStyle;
        }

        lock (_fontFaceStyleLock)
        {
            _cachedFontFaceStyle ??= BuildFontFaceStyle();
            return _cachedFontFaceStyle;
        }
    }

    private string BuildFontFaceStyle()
    {
        // Spike scope (Phase 1): only the emoji font is embedded so far. The system-safe-list
        // fonts (Liberation/DejaVu) are bundled in Phase 2 (FontCatalog, commit 1) and will extend
        // this same families list without changing the mechanism.
        var families = new[] { EmojiFamily };

        var faceRules = new StringBuilder();
        var stack = new List<string>();

        foreach (var family in families)
        {
            var bytes = _fonts.Resolve(family);
            if (bytes is not { Length: > 0 })
            {
                continue;
            }

            var base64 = Convert.ToBase64String(bytes);
            faceRules.Append("@font-face{font-family:'")
                .Append(family)
                .Append("';src:url(data:font/ttf;base64,")
                .Append(base64)
                .Append(");}");
            stack.Add($"'{family}'");
        }

        if (stack.Count == 0)
        {
            return string.Empty;
        }

        var style = new StringBuilder("<style>")
            .Append(faceRules)
            .Append("body{font-family:")
            .Append(string.Join(',', stack))
            .Append(",sans-serif;}")
            .Append("</style>");

        return style.ToString();
    }

    /// <summary>
    /// Evaluates the slot geometry script on the current Playwright page.
    /// Returns geometry entries for all <c>[data-slot]</c> elements, or an empty list
    /// if no slots are found or evaluation fails.
    /// </summary>
    private static async Task<IReadOnlyList<SlotGeometryEntry>> CaptureSlotGeometryAsync(
        IPage page,
        CancellationToken ct)
    {
        // $$eval: applies to all matching elements and returns a JS array.
        // The expression maps each element to a plain object with rect + computed-style fields.
        var rawEntries = await page.EvalOnSelectorAllAsync<SlotGeometryDto[]>(
            SlotGeometryScript.Selector,
            SlotGeometryScript.ExtractExpression);

        if (rawEntries is null || rawEntries.Length == 0)
        {
            return [];
        }

        return rawEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.Slot))
            .Select(e => new SlotGeometryEntry(
                Slot: e.Slot!,
                X: e.X,
                Y: e.Y,
                Width: e.Width,
                Height: e.Height,
                FontFamily: string.IsNullOrWhiteSpace(e.FontFamily) ? null : e.FontFamily,
                FontSizePx: e.FontSizePx,
                Color: string.IsNullOrWhiteSpace(e.Color) ? null : e.Color,
                TextAlign: string.IsNullOrWhiteSpace(e.TextAlign) ? null : e.TextAlign,
                FontWeight: string.IsNullOrWhiteSpace(e.FontWeight) ? null : e.FontWeight,
                FontStyle: string.IsNullOrWhiteSpace(e.FontStyle) ? null : e.FontStyle,
                LineHeightPx: e.LineHeightPx,
                LetterSpacingPx: e.LetterSpacingPx,
                TextTransform: string.IsNullOrWhiteSpace(e.TextTransform) ? null : e.TextTransform))
            .ToList();
    }

    /// <summary>
    /// Captures a background-only screenshot by hiding the content layer on the existing Playwright
    /// page, taking a screenshot, and then restoring visibility — all within the same page session.
    ///
    /// <para>
    /// WU-9 D3: the content layer (<c>[data-layer="content"]</c>) is hidden via an injected
    /// <c>&lt;style&gt;</c> tag. The background-only PNG is encoded as a
    /// <c>data:image/png;base64,…</c> data-URI and returned. On any failure the method returns
    /// <c>null</c> (non-fatal — export degrades to no background rather than crashing).
    /// </para>
    /// </summary>
    private static async Task<string?> CaptureBackgroundDataUriAsync(
        IPage page,
        VisualCompositionSpec spec,
        ILogger logger,
        CancellationToken ct)
    {
        const string HideContentScript = @"
            (function() {
                var el = document.querySelector('[data-layer=""content""]');
                if (el) { el._prevDisplay = el.style.display; el.style.display = 'none'; }
            })()";
        const string RestoreContentScript = @"
            (function() {
                var el = document.querySelector('[data-layer=""content""]');
                if (el) { el.style.display = el._prevDisplay !== undefined ? el._prevDisplay : ''; }
            })()";

        try
        {
            // Hide content layer
            await page.EvaluateAsync(HideContentScript);

            // Screenshot background only
            var bgBytes = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Type = ScreenshotType.Png,
                Clip = new Clip
                {
                    X = 0,
                    Y = 0,
                    Width = spec.Dimensions.Width,
                    Height = spec.Dimensions.Height
                },
                FullPage = false
            });

            if (bgBytes is null || bgBytes.Length == 0)
            {
                logger.LogWarning(
                    "Background capture returned empty bytes for format '{FormatKey}'; BackgroundImageDataUri will be null.",
                    spec.FormatKey);
                return null;
            }

            return "data:image/png;base64," + Convert.ToBase64String(bgBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Background capture failed for format '{FormatKey}'; BackgroundImageDataUri will be null. {Message}",
                spec.FormatKey,
                ex.Message);
            return null;
        }
        finally
        {
            // Best-effort restore — if this fails the page is about to be recycled anyway
            try
            {
                await page.EvaluateAsync(RestoreContentScript);
            }
            catch
            {
                // ignored — page will be returned to the pool regardless
            }
        }
    }

    /// <summary>
    /// Builds the per-render timeout <see cref="CancellationTokenSource"/>.
    /// <list type="bullet">
    ///   <item><description>Test seam <see cref="RenderingOptions.ForcePreExpiredTimeoutForTests"/>:
    ///   returns an already-cancelled source so the first awaited op deterministically times out.</description></item>
    ///   <item><description><see cref="RenderingOptions.PerRenderTimeoutSeconds"/> &gt; 0: a real deadline.</description></item>
    ///   <item><description>otherwise: an un-cancelled source — NO timeout (caller token still applies).</description></item>
    /// </list>
    /// </summary>
    private static CancellationTokenSource CreateTimeoutCts(RenderingOptions options)
    {
        if (options.ForcePreExpiredTimeoutForTests)
        {
            var preCancelled = new CancellationTokenSource();
            preCancelled.Cancel();
            return preCancelled;
        }

        return options.PerRenderTimeoutSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(options.PerRenderTimeoutSeconds))
            : new CancellationTokenSource();
    }

    /// <summary>Human-readable description of the active timeout for diagnostics.</summary>
    private static string DescribeTimeout(RenderingOptions options) =>
        options.ForcePreExpiredTimeoutForTests
            ? "0s (forced)"
            : $"{options.PerRenderTimeoutSeconds}s";
}

/// <summary>
/// DTO used to deserialize the JS object returned by <see cref="SlotGeometryScript.ExtractExpression"/>
/// via <c>EvalOnSelectorAllAsync</c>. Property names match the JavaScript camelCase keys.
/// Internal to the Infrastructure rendering namespace.
/// </summary>
file sealed class SlotGeometryDto
{
    public string? Slot { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? FontFamily { get; set; }
    public double FontSizePx { get; set; }
    public string? Color { get; set; }
    public string? TextAlign { get; set; }
    public string? FontWeight { get; set; }
    public string? FontStyle { get; set; }
    public double LineHeightPx { get; set; }
    public double LetterSpacingPx { get; set; }
    public string? TextTransform { get; set; }
}
