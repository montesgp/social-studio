using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// <see cref="IClientPostMediaArchiver"/> that downloads a client's transient post-image URLs and
/// re-hosts them durably via <see cref="IGeneratedAssetStorage"/>. Phase 2 (CAG2-PA-02).
/// </summary>
/// <remarks>
/// Per-image best-effort: a download or upload failure for one image is logged and skipped; the rest
/// are still archived. Apify presigned URLs expire, so the durable copy is what downstream generation
/// reads. Thumbnail downscaling is a follow-up optimization (stored as-downloaded for now).
/// </remarks>
public sealed class SupabasePostMediaArchiver : IClientPostMediaArchiver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<SupabasePostMediaArchiver> _logger;

    public SupabasePostMediaArchiver(
        IHttpClientFactory httpClientFactory,
        IGeneratedAssetStorage storage,
        ILogger<SupabasePostMediaArchiver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(storage);
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger ?? NullLogger<SupabasePostMediaArchiver>.Instance;
    }

    public async Task<IReadOnlyList<ProfilePostImageRef>> ArchiveAsync(
        Guid organizationId,
        Guid clientId,
        Guid analysisId,
        string sourcePostId,
        IReadOnlyList<string> imageUrls,
        CancellationToken ct)
    {
        if (imageUrls.Count == 0)
        {
            return [];
        }

        var refs = new List<ProfilePostImageRef>(imageUrls.Count);
        using var http = _httpClientFactory.CreateClient(nameof(SupabasePostMediaArchiver));
        // Hard per-download timeout: provider CDN URLs (e.g. Instagram) can hang/stall for server-side
        // requests. Without this the default 100s × N images can block the analysis worker for minutes.
        http.Timeout = TimeSpan.FromSeconds(20);

        for (var index = 0; index < imageUrls.Count; index++)
        {
            var url = imageUrls[index];
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            try
            {
                var bytes = await http.GetByteArrayAsync(url, ct);
                var mime = GuessMime(url);
                var ext = ExtensionFor(mime);
                var relativePath = $"aesthetic/{sourcePostId}-{index}{ext}";

                var stored = await _storage.PutAssetAsync(
                    new StoredAssetKey(organizationId, clientId, analysisId, relativePath),
                    new StoredAssetContent(bytes, mime),
                    ct);

                refs.Add(new ProfilePostImageRef(sourcePostId, index, stored.StoragePath, stored.ContentHash));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Best-effort: one image's failure must not lose the rest or fail the analysis.
                _logger.LogWarning(
                    ex,
                    "PostMediaArchiver: failed to archive image {Index} of post {PostId} (org {OrganizationId}). Skipping.",
                    index, sourcePostId, organizationId);
            }
        }

        return refs;
    }

    private static string GuessMime(string url)
    {
        var path = url.Split('?', 2)[0];
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
        if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return "image/webp";
        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return "image/gif";
        return "image/jpeg";
    }

    private static string ExtensionFor(string mime) => mime switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg",
    };
}
