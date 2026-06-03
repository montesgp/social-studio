namespace Incoders.Hive.Domain.Social.ProfileAnalysis;

/// <summary>
/// Encodes the allowed status transition table for <see cref="ProfileAnalysis"/>.
/// Pure static logic — no I/O, no EF references.
/// </summary>
public static class ProfileAnalysisStatusTransitions
{
    private static readonly HashSet<(ProfileAnalysisStatus From, ProfileAnalysisStatus To)> AllowedTransitions =
    [
        (ProfileAnalysisStatus.Requested, ProfileAnalysisStatus.ScrapingProfile),
        (ProfileAnalysisStatus.ScrapingProfile, ProfileAnalysisStatus.ProfileScraped),
        (ProfileAnalysisStatus.ScrapingProfile, ProfileAnalysisStatus.ProfileScrapedPartial),
        (ProfileAnalysisStatus.ProfileScraped, ProfileAnalysisStatus.Analyzing),
        (ProfileAnalysisStatus.ProfileScrapedPartial, ProfileAnalysisStatus.Analyzing),
        (ProfileAnalysisStatus.Analyzing, ProfileAnalysisStatus.Analyzed),

        // Any non-terminal status may transition to Failed.
        (ProfileAnalysisStatus.Requested, ProfileAnalysisStatus.Failed),
        (ProfileAnalysisStatus.ScrapingProfile, ProfileAnalysisStatus.Failed),
        (ProfileAnalysisStatus.ProfileScraped, ProfileAnalysisStatus.Failed),
        (ProfileAnalysisStatus.ProfileScrapedPartial, ProfileAnalysisStatus.Failed),
        (ProfileAnalysisStatus.Analyzing, ProfileAnalysisStatus.Failed),
    ];

    /// <summary>Returns <c>true</c> when transitioning from <paramref name="from"/> to
    /// <paramref name="to"/> is permitted by the domain model.</summary>
    public static bool IsValid(ProfileAnalysisStatus from, ProfileAnalysisStatus to) =>
        AllowedTransitions.Contains((from, to));

    /// <summary>Returns <c>true</c> for terminal statuses (<see cref="ProfileAnalysisStatus.Analyzed"/>
    /// and <see cref="ProfileAnalysisStatus.Failed"/>).</summary>
    public static bool IsTerminal(ProfileAnalysisStatus status) =>
        status is ProfileAnalysisStatus.Analyzed or ProfileAnalysisStatus.Failed;
}
