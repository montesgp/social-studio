using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Discovers real, high-performing content from the client's reference accounts and proposes it for
/// generation.
///
/// Moved from <c>ReferenceAccountContentDiscovery</c> (WU-3): now an <see cref="IContentDiscoverySource"/>,
/// <c>Order: 1</c>, <c>Supports("references")</c> only. This is the Content Generator's proposal
/// phase. It differs from <c>ReferenceAccountIdeaSource</c> on purpose: an idea source distils a post
/// down to a topic for the machine to write from, while a PROPOSAL keeps the whole post — metrics,
/// thumbnail, link — because a human is going to look at it and decide. Same scraping underneath,
/// different audience.
/// </summary>
public sealed class ReferenceAccountDiscoverySource : IContentDiscoverySource
{
    private const string InstagramNetwork = "instagram";
    private const string SourceKeyValue = "references";

    private readonly IClientBrandSnapshotLoader _snapshots;
    private readonly IProfileScrapeResolver _scraper;

    public ReferenceAccountDiscoverySource(
        IClientBrandSnapshotLoader snapshots,
        IProfileScrapeResolver scraper)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(scraper);

        _snapshots = snapshots;
        _scraper = scraper;
    }

    public string SourceKey => SourceKeyValue;

    public int Order => 1;

    public bool Supports(string searchCriterion) =>
        string.Equals(searchCriterion, SourceKeyValue, StringComparison.OrdinalIgnoreCase);

    public async Task<SourceDiscoveryResult> DiscoverAsync(
        ContentDiscoveryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxResults <= 0)
        {
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        var snapshot = await _snapshots.LoadAsync(request.OrganizationId, request.ClientId, ct);

        // Only Instagram is scrapeable today; filtering here rather than at the call site keeps the
        // caller free to tick networks that are not wired up yet without getting an error.
        var wantsInstagram = request.NetworkSources.Count == 0
            || request.NetworkSources.Any(n => string.Equals(n, InstagramNetwork, StringComparison.OrdinalIgnoreCase));

        if (!wantsInstagram)
        {
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        var referents = snapshot.ReferentAccounts
            .Where(account => string.Equals(account.NetworkCode, InstagramNetwork, StringComparison.OrdinalIgnoreCase))
            .Select(account => account.Handle ?? account.ProfileUrl)
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (referents.Count == 0)
        {
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        var found = new List<DiscoveredContent>();

        foreach (var handle in referents)
        {
            ct.ThrowIfCancellationRequested();

            var scrape = await _scraper.ResolveAsync(
                new ProfileScrapeRequest(
                    OrganizationId: request.OrganizationId,
                    AnalysisId: Guid.Empty,
                    ClientId: request.ClientId,
                    ProfileUrlOrHandle: handle,
                    PlatformKey: InstagramNetwork,
                    CapabilityKey: "profile"),
                ct);

            // One unreachable account must not empty the whole proposal.
            if (scrape.IsFailure)
            {
                continue;
            }

            foreach (var post in scrape.Value.OwnerPosts)
            {
                // A sponsored post performed because it was paid to. Proposing it would mislead the
                // operator about what this audience organically rewards.
                if (post.IsDegraded || post.IsPaidPartnership)
                {
                    continue;
                }

                found.Add(ToDiscovered(post, handle));
            }
        }

        // Strongest first, then capped: the operator's screen should open on the winners.
        var ranked = found
            .OrderByDescending(item => item.Views ?? -1)
            .ThenByDescending(item => (item.Likes ?? 0) + (item.Comments ?? 0))
            .Take(request.MaxResults)
            .ToList();

        return new SourceDiscoveryResult(SourceOutcome.Ran, ranked);
    }

    private static DiscoveredContent ToDiscovered(ProfilePost post, string handle)
    {
        var caption = post.Caption?.Trim();

        // Unlike an idea, a caption-less post is still a usable proposal — the operator judges it by
        // its metrics and thumbnail — so it gets a title instead of being dropped.
        var title = string.IsNullOrWhiteSpace(caption)
            ? $"Publicación de {handle}"
            : FirstLine(caption);

        return new DiscoveredContent(
            Title: title,
            Summary: caption ?? string.Empty,
            NetworkSource: InstagramNetwork,
            ContentUrl: $"https://www.instagram.com/p/{post.PostId}/",
            ThumbnailUrl: post.ImageUrls?.FirstOrDefault(),
            // WAS `post.VideoViewCount ?? 0` — that zero-fill is deleted by this change (D2): unavailable
            // stays null, never invented as a reported zero.
            Views: post.VideoViewCount,
            Likes: post.LikesCount,
            Comments: post.CommentsCount,
            Shares: null,
            Saves: null,
            PublishedAtUtc: post.Timestamp,
            SourceHandle: handle,
            SourceKey: SourceKeyValue);
    }

    private static string FirstLine(string caption)
    {
        var line = caption
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim() ?? caption;

        const int maxLength = 140;

        return line.Length <= maxLength ? line : line[..maxLength].TrimEnd();
    }
}
