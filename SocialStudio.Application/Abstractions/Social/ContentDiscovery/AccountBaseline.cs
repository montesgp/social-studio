namespace SocialStudio.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// A cached, author-level performance baseline (design §5.2, §6.5). Deliberately not a field on
/// <see cref="DiscoveredContent"/> — a candidate is a source observation; the baseline is a
/// separately-cached, separately-refreshed account property joined by (<see cref="NetworkKey"/>,
/// <see cref="AuthorHandle"/>) at scoring time.
///
/// <c>MedianViews</c>/<c>MedianEngagement</c> are nullable: a fresh account with no computable
/// median (e.g. no video posts) has a baseline row but no fabricated number — the scorer and
/// classifier degrade the relative-performance signal rather than treat a missing median as zero.
/// </summary>
public sealed record AccountBaseline(
    string NetworkKey,
    string AuthorHandle,
    double? MedianViews,
    double? MedianEngagement,
    int PostCount,
    DateTimeOffset ComputedAt)
{
    /// <summary>
    /// The ONE key builder used everywhere a baseline is looked up by (network, handle), so the
    /// scorer, the classifier and the repository can never disagree on identity (mirrors
    /// <see cref="CandidateIdentity"/>).
    /// </summary>
    public static string KeyFor(string networkKey, string authorHandle)
    {
        ArgumentNullException.ThrowIfNull(networkKey);
        ArgumentNullException.ThrowIfNull(authorHandle);

        return $"{networkKey.Trim().ToLowerInvariant()}:{authorHandle.Trim().ToLowerInvariant()}";
    }
}
