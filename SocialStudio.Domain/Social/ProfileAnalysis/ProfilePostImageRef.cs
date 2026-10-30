namespace SocialStudio.Domain.Social.ProfileAnalysis;

/// <summary>
/// A durably-hosted copy of a client post image, captured during profile analysis for
/// ClientAesthetic generation. Phase 2 (CAG2-PA-02).
/// </summary>
/// <remarks>
/// Apify image URLs are short-lived presigned links that expire, so the analysis pipeline
/// downloads the bytes and re-uploads them to durable object storage (Supabase Storage). This
/// value object is the durable reference that downstream generation reads as the visual signal
/// of the client's aesthetic. It is a pure Domain value type — no provider/storage SDK types.
/// </remarks>
/// <param name="SourcePostId">The <see cref="ProfilePost.PostId"/> the image was captured from.</param>
/// <param name="AssetIndex">Zero-based position within the source post: 0 = cover, 1.. = carousel children.</param>
/// <param name="StoragePath">The durable object-storage path of the re-hosted image.</param>
/// <param name="ContentHash">Content hash of the stored bytes; enables dedupe and integrity checks.</param>
public sealed record ProfilePostImageRef(
    string SourcePostId,
    int AssetIndex,
    string StoragePath,
    string ContentHash);
