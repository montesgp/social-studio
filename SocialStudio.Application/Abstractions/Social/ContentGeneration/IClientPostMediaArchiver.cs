using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port that re-hosts a client's transient post-image URLs as durable copies in object
/// storage. Phase 2 (CAG2-PA-02).
/// </summary>
/// <remarks>
/// Apify image URLs are short-lived presigned links. The implementation (Infrastructure) downloads
/// the bytes, downscales to a thumbnail, uploads to durable storage, and returns
/// <see cref="ProfilePostImageRef"/> handles. No provider/storage SDK types appear in this contract.
/// Per-image failures are best-effort: the implementation skips a failed image and returns the rest;
/// it MUST NOT throw for a single download/upload failure.
/// </remarks>
public interface IClientPostMediaArchiver
{
    /// <summary>
    /// Downloads and durably re-hosts the given post-image URLs under an
    /// <paramref name="organizationId"/>/<paramref name="clientId"/>/<paramref name="analysisId"/> path.
    /// </summary>
    /// <param name="organizationId">Owning organization.</param>
    /// <param name="clientId">Owning client.</param>
    /// <param name="analysisId">The profile analysis the images belong to.</param>
    /// <param name="sourcePostId">The post the URLs were captured from.</param>
    /// <param name="imageUrls">Transient image URLs (cover + carousel children).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Durable references for the images successfully archived (may be fewer than requested).</returns>
    Task<IReadOnlyList<ProfilePostImageRef>> ArchiveAsync(
        Guid organizationId,
        Guid clientId,
        Guid analysisId,
        string sourcePostId,
        IReadOnlyList<string> imageUrls,
        CancellationToken ct);
}
