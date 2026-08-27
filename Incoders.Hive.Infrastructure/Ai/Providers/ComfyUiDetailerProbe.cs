using System.Collections.Concurrent;
using System.Text.Json;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

/// <summary>
/// Tri-state result of probing a ComfyUI backend's <c>/object_info</c> for detailer-node support.
/// <see cref="Unknown"/> is the zero value so a forgotten branch never falls through to
/// <see cref="Supported"/> — an unhandled/default case behaves like a negative probe.
/// Refs: A1; CUI-07.
/// </summary>
internal enum ComfyUiDetailerSupport
{
    Unknown = 0,
    Supported,
    NotSupported,
}

/// <summary>
/// Answers whether a ComfyUI backend registers both the <c>FaceDetailer</c> and
/// <c>UltralyticsDetectorProvider</c> node classes, via the same <c>GET {baseUrl}/object_info</c>
/// call <see cref="ComfyUiModelCatalogClient"/> already makes. Never throws into the caller.
/// Results are cached per normalized base URL for <see cref="CacheTtl"/>; <see cref="ComfyUiDetailerSupport.Unknown"/>
/// results are never cached, and a static <see cref="ConcurrentDictionary{TKey,TValue}"/> is used
/// because <c>ComfyUiImageModelClient</c> is resolved per-request via <c>ExternalProviderClientFactory</c>,
/// so instance fields would cache nothing (mirrors <c>AiCompletionGateway.SchemaCache</c>).
/// Refs: A1, A2, A3, A4; CUI-07, CUI-09.
/// </summary>
internal static class ComfyUiDetailerProbe
{
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    internal const string FaceDetailerClass = "FaceDetailer";
    internal const string DetectorProviderClass = "UltralyticsDetectorProvider";
    internal static readonly IReadOnlySet<string> DetailerNodeIds = new HashSet<string> { "10", "11" };

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();

    internal static Task<ComfyUiDetailerSupport> ProbeAsync(
        HttpClient http,
        string baseUrl,
        CancellationToken cancellationToken = default) =>
        ProbeAsync(http, baseUrl, TimeProvider.System, cancellationToken);

    internal static async Task<ComfyUiDetailerSupport> ProbeAsync(
        HttpClient http,
        string baseUrl,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(baseUrl);
        var now = timeProvider.GetUtcNow();

        if (Cache.TryGetValue(key, out var cached) && now - cached.ResolvedAt < CacheTtl)
        {
            return cached.Support;
        }

        var support = await ProbeUncachedAsync(http, key, cancellationToken);

        // A4: transport failures / malformed responses (Unknown) are never written to the cache —
        // a transient blip must not pin a capable backend to plain for the full TTL.
        if (support != ComfyUiDetailerSupport.Unknown)
        {
            Cache[key] = new CacheEntry(support, now);
        }

        return support;
    }

    /// <summary>
    /// A5: drops a cached <see cref="ComfyUiDetailerSupport.Supported"/> entry so the next job for
    /// this base URL re-probes instead of waiting out the TTL. No-op when nothing is cached.
    /// </summary>
    internal static void Invalidate(string baseUrl) => Cache.TryRemove(NormalizeKey(baseUrl), out _);

    /// <summary>
    /// True when a <c>/prompt</c> rejection payload's <c>node_errors</c> keys intersect
    /// <see cref="DetailerNodeIds"/>, or the payload names either detailer class directly.
    /// </summary>
    internal static bool IsDetailerAttributable(string? promptRejectionPayload)
    {
        if (string.IsNullOrWhiteSpace(promptRejectionPayload))
        {
            return false;
        }

        return DetailerNodeIds.Any(id => promptRejectionPayload.Contains($"\"{id}\":", StringComparison.Ordinal))
            || promptRejectionPayload.Contains(FaceDetailerClass, StringComparison.Ordinal)
            || promptRejectionPayload.Contains(DetectorProviderClass, StringComparison.Ordinal);
    }

    internal static void ResetCacheForTests() => Cache.Clear();

    private static async Task<ComfyUiDetailerSupport> ProbeUncachedAsync(
        HttpClient http,
        string normalizedBaseUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.GetAsync($"{normalizedBaseUrl}/object_info", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ComfyUiDetailerSupport.Unknown;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ComfyUiDetailerSupport.Unknown;
            }

            var hasFaceDetailer = doc.RootElement.TryGetProperty(FaceDetailerClass, out _);
            var hasDetectorProvider = doc.RootElement.TryGetProperty(DetectorProviderClass, out _);

            return hasFaceDetailer && hasDetectorProvider
                ? ComfyUiDetailerSupport.Supported
                : ComfyUiDetailerSupport.NotSupported;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // CUI-07-c: unreachable server, timeout, non-JSON body, or any other unexpected
            // failure — never propagate into the generation path.
            return ComfyUiDetailerSupport.Unknown;
        }
    }

    private static string NormalizeKey(string baseUrl) => baseUrl.TrimEnd('/');

    private sealed record CacheEntry(ComfyUiDetailerSupport Support, DateTimeOffset ResolvedAt);
}
