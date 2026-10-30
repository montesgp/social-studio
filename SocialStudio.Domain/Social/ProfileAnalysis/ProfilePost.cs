namespace SocialStudio.Domain.Social.ProfileAnalysis;

/// <summary>
/// Value object for a single post entry from the Apify profile actor's latestPosts array.
/// </summary>
/// <remarks>
/// IMPORTANT — WU-01 spike findings:
/// - <c>latestReels[]</c> is always empty in instagram-profile-scraper mode.
///   Reels shared to feed appear as latestPosts items with mediaType == "Video".
/// - <see cref="VideoViewCount"/> is populated for Video posts (reels-on-feed).
/// - videoPlayCount is NOT populated (always null in profile-scraper mode) — do NOT use it.
/// - videoDuration is also always null in profile-scraper mode.
/// - Median-views computation anchors exclusively on <see cref="VideoViewCount"/>.
/// </remarks>
/// <param name="PostId">Unique post identifier from the actor response.</param>
/// <param name="MediaType">Media type: "Image", "Video", "Reel", or "Carousel".</param>
/// <param name="LikesCount">Like count at scrape time.</param>
/// <param name="CommentsCount">Comment count at scrape time.</param>
/// <param name="VideoViewCount">View count for Video/Reel posts; null for Image/Carousel.
///     The only view count populated in profile-scraper mode — videoPlayCount is always null.</param>
/// <param name="Timestamp">Post publication timestamp (UTC).</param>
/// <param name="OwnerId">The numeric Instagram owner id string. Used for owner-filter logic.</param>
/// <param name="OwnerUsername">The owner's Instagram handle.</param>
/// <param name="IsPaidPartnership">True when the post is tagged as a paid partnership.</param>
/// <param name="IsDegraded">True when the Apify actor marked this post as degraded
///     (<c>_degraded == true</c>). Degraded posts MUST be excluded from metric computations.</param>
/// <param name="Hashtags">Hashtags extracted from the post caption.</param>
/// <param name="CarouselChildrenCount">Number of child media items in a Carousel post, as reported
///     by the Apify actor (childrenCount field). Null when absent or for non-carousel posts.
///     Used to compute AverageSlidesPerCarousel in server-derived stats (CAG-PA-01).</param>
/// <param name="ImageUrls">Transient image URLs captured from the actor (the post cover via
///     <c>displayUrl</c>, plus carousel children via <c>displayResources</c>). Phase 2 (CAG2-PA-01).
///     Null/empty when the configured actor does not return media. These are short-lived provider
///     URLs — they are downloaded and re-hosted durably (<see cref="ProfilePostImageRef"/>) before use.</param>
/// <param name="Caption">The post caption text, when the actor returns it. Phase 2 (CAG2-PA-01).
///     Null when absent. Feeds caption-derived voice/style inference.</param>
public sealed record ProfilePost(
    string PostId,
    string MediaType,
    long LikesCount,
    long CommentsCount,
    long? VideoViewCount,
    DateTimeOffset Timestamp,
    string OwnerId,
    string OwnerUsername,
    bool IsPaidPartnership,
    bool IsDegraded,
    IReadOnlyList<string> Hashtags,
    int? CarouselChildrenCount = null,
    IReadOnlyList<string>? ImageUrls = null,
    string? Caption = null)
{
    /// <summary>
    /// Returns <c>true</c> when this post was authored by the account being analysed.
    /// Owner filtering must be applied BEFORE any engagement, cadence, or weekday aggregations.
    /// </summary>
    /// <param name="profileInstagramId">The <see cref="ProfileSnapshot.InstagramId"/> of the analysed account.</param>
    public bool IsOwnerPost(string profileInstagramId) =>
        !string.IsNullOrWhiteSpace(profileInstagramId)
        && string.Equals(OwnerId, profileInstagramId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <c>true</c> when this post is a Video or Reel type.
    /// Reels shared to feed appear as mediaType=="Video" in instagram-profile-scraper mode.
    /// Only Video/Reel posts contribute to the median-views computation (PA-09).
    /// Carousel posts are excluded from median-views even if videoViewCount is accidentally non-null.
    /// </summary>
    public bool IsVideo =>
        string.Equals(MediaType, "Video", StringComparison.OrdinalIgnoreCase)
        || string.Equals(MediaType, "Reel", StringComparison.OrdinalIgnoreCase);
}
