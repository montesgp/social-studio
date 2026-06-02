using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Abstractions.Social;

/// <summary>
/// Port for server-side metric computation from owner-filtered post data.
/// Pure — no I/O, no external dependencies, no EF types. Implemented by
/// <c>ProfileMetricCalculator</c> in the Application layer.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Owner-filter posts by ownerId == profile.instagramId (PA-07).
/// - Exclude posts where IsDegraded == true from all computations (PA-08).
/// - Return <see cref="ProfileAnalysisErrors.NoUsableOwnerPosts"/> when no usable posts remain.
/// - Compute median views from Video/Reel posts only; null when none exist (PA-09).
/// - Bucket weekday averages from UTC-normalised timestamps (PA-12).
/// - Compute content-type mix percentages and posting cadence.
/// </remarks>
public interface IProfileMetricCalculator
{
    /// <summary>
    /// Computes server-derived stats from the raw post list and profile snapshot.
    /// </summary>
    /// <param name="allPosts">
    /// All posts returned by the scraper (owner + tagged + coauthored).
    /// The calculator applies owner-filter and degrade-filter internally.
    /// </param>
    /// <param name="profile">Profile snapshot carrying the instagramId for owner filtering.</param>
    /// <returns>
    /// A <see cref="ProfileStats"/> record on success, or
    /// <see cref="ProfileAnalysisErrors.NoUsableOwnerPosts"/> when all owner posts are degraded.
    /// </returns>
    Result<ProfileStats> Compute(
        IReadOnlyList<ProfilePost> allPosts,
        ProfileSnapshot profile);
}
