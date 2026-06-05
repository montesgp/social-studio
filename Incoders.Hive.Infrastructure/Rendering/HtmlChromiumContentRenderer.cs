using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
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
public sealed class HtmlChromiumContentRenderer : IContentRenderer
{
    private readonly ChromiumBrowserPool? _browserPool;
    private readonly IContentRenderer _fallbackRenderer;
    private readonly RenderingOptions _options;

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
    public HtmlChromiumContentRenderer(
        ChromiumBrowserPool? browserPool,
        IContentRenderer fallbackRenderer,
        RenderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(fallbackRenderer);
        ArgumentNullException.ThrowIfNull(options);

        _browserPool = browserPool;
        _fallbackRenderer = fallbackRenderer;
        _options = options;
    }

    /// <inheritdoc />
    public Task<Result<RenderedAsset>> RenderAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        // Engine config governs: Skia always delegates to fallback (even for Html specs).
        if (_options.Engine == RenderingEngine.Skia)
        {
            return _fallbackRenderer.RenderAsync(spec, opts, ct);
        }

        // Non-HTML output kinds: delegate to fallback.
        if (spec.OutputKind != CompositionOutputKind.Html)
        {
            return _fallbackRenderer.RenderAsync(spec, opts, ct);
        }

        // Chromium engine + Html spec: render via Playwright.
        return RenderChromiumAsync(spec, opts, ct);
    }

    private async Task<Result<RenderedAsset>> RenderChromiumAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        if (_browserPool is null)
        {
            return Result.Failure<RenderedAsset>(
                RenderErrors.RenderFailed("ChromiumBrowserPool is null — engine is Chromium but pool was not injected."));
        }

        var markup = spec.DesignerMarkup;
        if (string.IsNullOrWhiteSpace(markup))
        {
            return Result.Failure<RenderedAsset>(
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
                return Result.Failure<RenderedAsset>(
                    RenderErrors.RenderFailed("Playwright screenshot returned empty bytes."));
            }

            var asset = RenderedAsset.Create(
                spec.FormatKey,
                "image/png",
                $"{spec.FormatKey}.png",
                screenshotBytes);

            return Result.Success(asset);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Result.Failure<RenderedAsset>(
                RenderErrors.RenderFailed(
                    $"Chromium render timeout after {DescribeTimeout(_options)} for format '{spec.FormatKey}'."));
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<RenderedAsset>(
                RenderErrors.RenderFailed("Chromium render cancelled by caller."));
        }
        catch (PlaywrightException ex)
        {
            return Result.Failure<RenderedAsset>(
                RenderErrors.RenderFailed($"Playwright error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Result.Failure<RenderedAsset>(
                RenderErrors.RenderFailed($"Unexpected render error: {ex.Message}"));
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
