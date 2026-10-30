namespace SocialStudio.Infrastructure.Rendering;

/// <summary>
/// Selects which rendering engine to use for <c>IContentRenderer</c>.
/// Chromium is the primary HTML/CSS engine (B1b). Skia is the fallback (SkiaSharp slot raster).
/// Configurable via <c>Rendering:Engine</c> in appsettings.
/// </summary>
public enum RenderingEngine
{
    /// <summary>
    /// Default. HTML/CSS rendered via headless Chromium (Playwright .NET).
    /// Requires <c>playwright install chromium</c> to be run in the container.
    /// </summary>
    Chromium,

    /// <summary>
    /// SkiaSharp slot-raster fallback. No external browser required.
    /// Used when Chromium is not available or for non-designed formats.
    /// </summary>
    Skia
}

/// <summary>
/// Configuration options for the Infrastructure rendering pipeline (B1b).
/// Bound from configuration section <c>Rendering</c>.
/// Design: Decision 11 (pooled Chromium browser, DeviceScaleFactor, concurrency, timeout).
/// </summary>
public sealed class RenderingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Rendering";

    /// <summary>
    /// Selects the rendering engine.
    /// Default: <see cref="RenderingEngine.Chromium"/>.
    /// Override with <c>"Skia"</c> to fall back to the SkiaSharp renderer.
    /// </summary>
    public RenderingEngine Engine { get; set; } = RenderingEngine.Chromium;

    /// <summary>
    /// Maximum number of concurrent Chromium renders allowed.
    /// Bounded by <c>SemaphoreSlim</c> in <see cref="ChromiumBrowserPool"/>.
    /// Default: 2. Tune to available container memory (Chromium ~150 MB per context).
    /// </summary>
    public int MaxConcurrentRenders { get; set; } = 2;

    /// <summary>
    /// Per-render wall-clock timeout in seconds.
    /// <c>0</c> (or any non-positive value) = <b>no timeout</b> — the render is bounded only by the
    /// caller's <see cref="CancellationToken"/>. This is the documented "disabled" semantic.
    /// Default: 15. Set lower in resource-constrained environments.
    /// </summary>
    /// <remarks>
    /// To exercise the timeout PATH deterministically in tests, use the internal
    /// <see cref="ForcePreExpiredTimeoutForTests"/> seam rather than a tiny wall-clock value
    /// (sub-second timers are racy on Windows due to ~15 ms timer resolution).
    /// </remarks>
    public int PerRenderTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Test-only seam: when <c>true</c>, the renderer treats the per-render timeout as already
    /// elapsed (a deadline in the past), so the very first awaited render operation observes the
    /// timeout deterministically. This exists solely so integration tests can verify the timeout
    /// path WITHOUT a racy sub-second wall-clock value or a confusing "0 means immediate" sentinel.
    /// Never set in production (config binding never sets it; it has no config key).
    /// </summary>
    internal bool ForcePreExpiredTimeoutForTests { get; set; }

    /// <summary>
    /// Chromium device scale factor applied to all renders.
    /// Default: 2 — doubles the physical pixel count for retina-quality output.
    /// A 1080×1920 logical canvas becomes a 2160×3840 physical pixel PNG.
    /// Design: Decision 11.
    /// </summary>
    public int DeviceScaleFactor { get; set; } = 2;
}
