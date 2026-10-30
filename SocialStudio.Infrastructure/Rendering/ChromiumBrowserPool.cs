using Microsoft.Playwright;

namespace SocialStudio.Infrastructure.Rendering;

/// <summary>
/// Singleton pooled Chromium browser service.
/// Owns ONE <see cref="IBrowser"/> instance, shared across all renders.
/// Per-render lifecycle: acquire semaphore slot → create fresh <c>IBrowserContext</c> +
/// <c>IPage</c> → render → dispose context. The browser itself is never closed mid-run.
///
/// <para>
/// Deploy prerequisite: <c>playwright install chromium</c> must be run in the container image
/// before this pool is initialized. See DEPLOY-NOTES.md.
/// </para>
///
/// <para>
/// Design: Decision 11 (pooled singleton browser, SemaphoreSlim bounded concurrency,
/// DeviceScaleFactor=2, per-render timeout).
/// </para>
/// </summary>
public sealed class ChromiumBrowserPool : IAsyncDisposable
{
    private readonly RenderingOptions _options;
    private readonly SemaphoreSlim _semaphore;

    // Guarded by _initLock — lazy browser launch
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _initialized;
    private bool _disposed;

    public ChromiumBrowserPool(RenderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _semaphore = new SemaphoreSlim(
            Math.Max(1, options.MaxConcurrentRenders),
            Math.Max(1, options.MaxConcurrentRenders));
    }

    /// <summary>
    /// Returns the number of render slots currently available (observable for tests).
    /// </summary>
    public int AvailableRenderSlots => _semaphore.CurrentCount;

    /// <summary>
    /// Attempts to acquire one render slot without blocking. Returns <c>true</c> if successful.
    /// Used by tests to verify bounded concurrency without launching a real browser.
    /// </summary>
    public bool TryAcquireImmediately() => _semaphore.Wait(0);

    /// <summary>Releases one render slot.</summary>
    public void Release() => _semaphore.Release();

    /// <summary>
    /// Initializes the Playwright instance and launches Chromium.
    /// Must be called once before <see cref="AcquirePageAsync"/> is used.
    /// Safe to call multiple times (idempotent via lock).
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                // Required for Linux containers without sandboxing support
                Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
            });
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Acquires a semaphore slot and creates a fresh <c>IBrowserContext</c> for one render.
    /// The caller MUST dispose the returned <see cref="RenderLease"/> when done.
    /// </summary>
    /// <param name="width">Logical canvas width in CSS pixels.</param>
    /// <param name="height">Logical canvas height in CSS pixels.</param>
    /// <param name="cancellationToken">Cancellation token for the acquire wait.</param>
    /// <returns>A lease containing the page and a dispose action that releases the slot.</returns>
    public async Task<RenderLease> AcquirePageAsync(int width, int height, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ChromiumBrowserPool));

        // Lazy-initialize on first use. The pool is a singleton with no startup hook, so the first
        // render launches Playwright + Chromium here; InitializeAsync is idempotent (guarded by
        // _initLock) so concurrent first-renders are safe and later calls return immediately.
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }

        // Acquire concurrency slot — blocks until a slot is free
        await _semaphore.WaitAsync(cancellationToken);

        IBrowserContext? context = null;
        try
        {
            context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                DeviceScaleFactor = _options.DeviceScaleFactor
            });

            var page = await context.NewPageAsync();
            return new RenderLease(page, context, _semaphore);
        }
        catch
        {
            // If context creation fails, release the semaphore slot before re-throwing
            if (context is not null)
            {
                try { await context.DisposeAsync(); } catch { /* best-effort */ }
            }
            _semaphore.Release();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_browser is not null)
        {
            try { await _browser.DisposeAsync(); } catch { /* best-effort */ }
        }

        _playwright?.Dispose();

        _semaphore.Dispose();
        _initLock.Dispose();
    }
}

/// <summary>
/// A scoped render lease that owns one <see cref="IPage"/> for a single Chromium render.
/// Disposing this lease disposes the browser context and releases the concurrency semaphore slot.
/// </summary>
public sealed class RenderLease : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly SemaphoreSlim _semaphore;
    private bool _disposed;

    internal RenderLease(IPage page, IBrowserContext context, SemaphoreSlim semaphore)
    {
        Page = page;
        _context = context;
        _semaphore = semaphore;
    }

    /// <summary>The Playwright page for this render.</summary>
    public IPage Page { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await _context.DisposeAsync();
        }
        catch
        {
            // Best-effort dispose
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
