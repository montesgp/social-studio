namespace Incoders.Hive.Domain.Social.ProfileAnalysis;

/// <summary>
/// Value object carrying the profile-level fields scraped from the Apify profile actor.
/// All fields reflect the provider response verbatim; no EF / JSON types referenced.
/// </summary>
/// <param name="InstagramId">The numeric Instagram owner id string (ownerId in latestPosts).</param>
/// <param name="Username">Instagram handle without the '@' prefix.</param>
/// <param name="FollowersCount">Follower count at scrape time.</param>
/// <param name="PostsCount">Total published posts count at scrape time.</param>
/// <param name="ReelsCount">Reels count as returned by the actor; may be null (latestReels[] is
///     always empty in instagram-profile-scraper mode — see WU-01 spike findings).</param>
/// <param name="EngagementRate">Engagement rate percentage as returned by the actor.</param>
/// <param name="MedianDaysBetweenPosts">Median posting cadence in days; null when insufficient data.</param>
/// <param name="ProfilePicUrl">Profile picture URL.</param>
/// <param name="Biography">Account biography text.</param>
/// <param name="IsVerified">Whether the account has a verified badge.</param>
/// <param name="IsBusinessAccount">Whether the account is registered as a business.</param>
/// <param name="BusinessCategoryName">Business category label; null for personal accounts.</param>
public sealed record ProfileSnapshot(
    string InstagramId,
    string Username,
    long FollowersCount,
    long PostsCount,
    long? ReelsCount,
    double EngagementRate,
    double? MedianDaysBetweenPosts,
    string? ProfilePicUrl,
    string? Biography,
    bool IsVerified,
    bool IsBusinessAccount,
    string? BusinessCategoryName);
