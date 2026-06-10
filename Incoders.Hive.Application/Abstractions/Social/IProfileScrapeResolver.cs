using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Abstractions.Social;

/// <summary>
/// Port for resolving a full Instagram profile scrape — profile metadata + post dataset.
/// Implemented by Infrastructure (ApifyProfileScrapeResolver with automatic backup-actor fallback).
/// MUST NOT reference any provider, EF, HTTP, or JSON types.
/// </summary>
public interface IProfileScrapeResolver
{
    Task<Result<ProfileScrapeResult>> ResolveAsync(
        ProfileScrapeRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Input contract for <see cref="IProfileScrapeResolver"/>.
/// Carries only stable identifiers — no provider-specific types.
/// </summary>
/// <param name="OrganizationId">The owning organization (for actor-mapping lookup and diagnostics).</param>
/// <param name="AnalysisId">The <see cref="ProfileAnalysis"/> record id (correlates diagnostics).</param>
/// <param name="ClientId">Optional client id; null for URL-path analyses.</param>
/// <param name="ProfileUrlOrHandle">The Instagram handle or full profile URL to scrape.</param>
/// <param name="PlatformKey">Platform discriminator — always "instagram" for this flow.</param>
/// <param name="CapabilityKey">Capability key used to route the actor mapping — always "profile".</param>
public sealed record ProfileScrapeRequest(
    Guid OrganizationId,
    Guid AnalysisId,
    Guid? ClientId,
    string ProfileUrlOrHandle,
    string PlatformKey,
    string CapabilityKey,
    IReadOnlyList<string>? PreferredActorIds = null);

/// <summary>
/// Output contract from <see cref="IProfileScrapeResolver"/>.
/// </summary>
/// <param name="Profile">Profile-level snapshot fields (instagramId, username, followers, etc.).</param>
/// <param name="OwnerPosts">
/// Owner-filtered post list from latestPosts[]. Per WU-01 spike findings, latestReels[] is always
/// empty in instagram-profile-scraper mode — reels-on-feed appear here as mediaType=="Video".
/// </param>
/// <param name="Reels">
/// Direct-from-reels endpoint items when available; always empty in instagram-profile-scraper mode
/// (WU-01 spike finding). Included for forward-compatibility. Mapped defensively.
/// </param>
/// <param name="ProviderAggregates">
/// Provider-level aggregate fields returned by Apify (mediaCount, followersCount as reported by
/// the actor, engagement data). Carried for diagnostics; not used in metric computation.
/// </param>
/// <param name="IsPartial">
/// True when some owner posts were degraded (marked _degraded by the actor) or when the
/// backup actor was used. The analysis proceeds on partial data and transitions to
/// ProfileScrapedPartial instead of ProfileScraped.
/// </param>
/// <param name="Diagnostics">
/// Per-attempt provider failure records. Populated even on success when fallback was used
/// (primary actor failure + backup actor success).
/// </param>
/// <param name="UsedActorId">The actor id that actually produced the successful result.</param>
/// <param name="FallbackUsed">True when the backup actor was used (primary failed).</param>
public sealed record ProfileScrapeResult(
    ProfileSnapshot Profile,
    IReadOnlyList<ProfilePost> OwnerPosts,
    IReadOnlyList<ProfilePost> Reels,
    ProfileAggregates ProviderAggregates,
    bool IsPartial,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics,
    string UsedActorId = "",
    bool FallbackUsed = false);

/// <summary>
/// Provider-level aggregate fields from the Apify profile actor response.
/// Not used in server-side metric computation — carried for diagnostics and raw storage.
/// </summary>
/// <param name="MediaCount">Total media count as reported by the actor.</param>
/// <param name="FollowersCount">Followers count as reported by the actor.</param>
/// <param name="FollowingCount">Following count as reported by the actor.</param>
/// <param name="PostsReceived">Total posts returned in the latestPosts[] array before owner filtering.</param>
/// <param name="PostsProcessed">Total owner posts processed after filtering and degrade-exclusion.</param>
/// <param name="DegradedPostCount">Count of posts excluded because _degraded == true.</param>
public sealed record ProfileAggregates(
    long MediaCount,
    long FollowersCount,
    long FollowingCount,
    int PostsReceived,
    int PostsProcessed,
    int DegradedPostCount);
