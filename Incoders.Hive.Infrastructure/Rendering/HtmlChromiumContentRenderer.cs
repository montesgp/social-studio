using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace Incoders.Hive.Infrastructure.Rendering;

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
            return {
                slot: el.getAttribute('data-slot') || '',
                x: r.x,
                y: r.y,
                width: r.width,
                height: r.height,
                fontFamily: ff || null,
                fontSizePx: fsPx,
                color: s.color || null,
                textAlign: s.textAlign || null
            };
        })
    ";
}

public sealed class HtmlChromiumContentRenderer : IContentRendererWithGeometry
{
    private readonly ChromiumBrowserPool? _browserPool;
    private readonly IContentRenderer _fallbackRenderer;
    private readonly RenderingOptions _options;
    private readonly ILogger<HtmlChromiumContentRenderer> _logger;

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
    public HtmlChromiumContentRenderer(
        ChromiumBrowserPool? browserPool,
        IContentRenderer fallbackRenderer,
        RenderingOptions options,
        ILogger<HtmlChromiumContentRenderer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fallbackRenderer);
        ArgumentNullException.ThrowIfNull(options);

        _browserPool = browserPool;
        _fallbackRenderer = fallbackRenderer;
        _options = options;
        _logger = logger ?? NullLogger<HtmlChromiumContentRenderer>.Instance;
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
            await page.SetContentAsync(markup, new PageSetContentOptions
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

            return Result.Success(new RenderedAssetWithGeometry(asset, slotGeometry));
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
                TextAlign: string.IsNullOrWhiteSpace(e.TextAlign) ? null : e.TextAlign))
            .ToList();
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
}
