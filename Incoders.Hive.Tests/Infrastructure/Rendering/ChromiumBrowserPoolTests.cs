using Incoders.Hive.Infrastructure.Rendering;

namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// B1b TDD tests for <see cref="ChromiumBrowserPool"/> lifecycle and concurrency seams.
/// Tasks: 7.3 (pool reuse + concurrency bound).
/// These tests use mock-friendly overloads — they do NOT require a real Chromium installation.
/// Design: Decision 11 (pooled singleton browser, SemaphoreSlim bounded concurrency).
/// </summary>
public sealed class ChromiumBrowserPoolTests
{
    // -----------------------------------------------------------------------
    // Task 7.3 RED: browser reuse
    // -----------------------------------------------------------------------

    /// <summary>
    /// The pool must not exceed its configured concurrency limit.
    /// When maxConcurrentRenders=1, a second concurrent render request blocks until the first releases.
    /// This test uses the semaphore-count observable on the pool to verify the bound.
    /// </summary>
    [Fact]
    public void BrowserPool_ConcurrencyBoundedBySemaphore()
    {
        // GIVEN a pool with maxConcurrentRenders=1
        var options = new RenderingOptions { MaxConcurrentRenders = 1 };
        var pool = new ChromiumBrowserPool(options);

        // WHEN we acquire the first slot
        var acquired = pool.TryAcquireImmediately();

        // THEN the pool reports no remaining capacity
        Assert.True(acquired, "First acquire should succeed immediately.");
        Assert.Equal(0, pool.AvailableRenderSlots);

        // Cleanup: release slot so the object can be garbage-collected cleanly
        pool.Release();
    }

    /// <summary>
    /// After releasing a slot, available capacity returns to max.
    /// </summary>
    [Fact]
    public void BrowserPool_AfterRelease_CapacityRestored()
    {
        // GIVEN a pool with maxConcurrentRenders=2
        var options = new RenderingOptions { MaxConcurrentRenders = 2 };
        var pool = new ChromiumBrowserPool(options);

        // WHEN we acquire then release
        pool.TryAcquireImmediately();
        pool.Release();

        // THEN capacity is fully restored
        Assert.Equal(2, pool.AvailableRenderSlots);
    }

    /// <summary>
    /// Regression: <see cref="ChromiumBrowserPool.AcquirePageAsync"/> must lazily initialize the
    /// pool on first use. The pool is a singleton with no startup hook, so before this fix the
    /// first render threw "ChromiumBrowserPool must be initialized before use" and storys/carousels
    /// never rendered (the failure was masked earlier by the designer ProviderCallContext bug).
    /// </summary>
    [Fact]
    public async Task AcquirePageAsync_WithoutExplicitInitialize_LazilyInitializesAndReturnsLease()
    {
        if (!ChromiumAvailabilityCheck.IsInstalled())
        {
            // Graceful skip — environments without a Chromium install can't launch the browser.
            return;
        }

        var options = new RenderingOptions { Engine = RenderingEngine.Chromium, MaxConcurrentRenders = 1 };
        await using var pool = new ChromiumBrowserPool(options);

        // InitializeAsync is intentionally NOT called — AcquirePageAsync must self-initialize.
        await using var lease = await pool.AcquirePageAsync(400, 400, CancellationToken.None);

        Assert.NotNull(lease.Page);
    }

    /// <summary>
    /// DeviceScaleFactor default is 2 per design decision 11.
    /// </summary>
    [Fact]
    public void RenderingOptions_DefaultDeviceScaleFactor_IsTwo()
    {
        var options = new RenderingOptions();
        Assert.Equal(2, options.DeviceScaleFactor);
    }

    /// <summary>
    /// Default MaxConcurrentRenders is 2 per design decision 11.
    /// </summary>
    [Fact]
    public void RenderingOptions_DefaultMaxConcurrentRenders_IsTwo()
    {
        var options = new RenderingOptions();
        Assert.Equal(2, options.MaxConcurrentRenders);
    }

    /// <summary>
    /// Default engine is Chromium per design decision 11.
    /// </summary>
    [Fact]
    public void RenderingOptions_DefaultEngine_IsChromium()
    {
        var options = new RenderingOptions();
        Assert.Equal(RenderingEngine.Chromium, options.Engine);
    }
}
