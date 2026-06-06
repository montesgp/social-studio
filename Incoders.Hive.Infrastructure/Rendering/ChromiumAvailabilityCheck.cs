using Microsoft.Playwright;

namespace Incoders.Hive.Infrastructure.Rendering;

/// <summary>
/// Utility to detect whether a Chromium browser is installed for Playwright.
/// Used by integration tests to gracefully skip browser-gated tests when Chromium is absent
/// (e.g. in CI environments without a browser install step, or developer machines).
///
/// <para>
/// This mirrors the skip pattern used elsewhere in the test suite (e.g. DbStateInspectionTests).
/// </para>
/// </summary>
public static class ChromiumAvailabilityCheck
{
    private static bool? _cached;

    /// <summary>
    /// Returns <c>true</c> if a Chromium executable can be found by Playwright.
    /// Result is cached after the first call.
    /// </summary>
    public static bool IsInstalled()
    {
        if (_cached.HasValue) return _cached.Value;

        try
        {
            // Playwright resolves the browser path without launching it.
            // If no browser is installed, PlaywrightException is thrown with a path-not-found message.
            var pw = Playwright.CreateAsync().GetAwaiter().GetResult();
            try
            {
                // BrowserType.ExecutablePath is the standard way to detect installation.
                var execPath = pw.Chromium.ExecutablePath;
                _cached = !string.IsNullOrWhiteSpace(execPath) && File.Exists(execPath);
            }
            finally
            {
                pw.Dispose();
            }
        }
        catch
        {
            _cached = false;
        }

        return _cached.Value;
    }
}
