using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Domain.Social.ClientOwnAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Discovers real, high-performing content from the client's OWN accounts (CR-DISC-02).
///
/// A near-clone of <see cref="ReferenceAccountDiscoverySource"/>: same <see cref="IProfileScrapeResolver"/>
/// loop, but fed from <see cref="IClientOwnAccountRepository"/> instead of the referent list. Own
/// accounts carry a <see cref="SocialNetworkId"/> foreign key rather than a network code string, so
/// this source resolves the code via <see cref="ISocialNetworkRepository"/> before filtering to
/// Instagram — the only network scrapeable today (same limitation as the reference source).
/// <c>Order: 2</c>, <c>Supports("own")</c> only.
/// </summary>
public sealed class OwnAccountDiscoverySource : IContentDiscoverySource
{
    private const string InstagramNetwork = "instagram";
    private const string SourceKeyValue = "own";

    private readonly IClientOwnAccountRepository _accounts;
    private readonly ISocialNetworkRepository _networks;
    private readonly IProfileScrapeResolver _scraper;

    public OwnAccountDiscoverySource(
        IClientOwnAccountRepository accounts,
        ISocialNetworkRepository networks,
        IProfileScrapeResolver scraper)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(networks);
        ArgumentNullException.ThrowIfNull(scraper);

        _accounts = accounts;
        _networks = networks;
        _scraper = scraper;
    }

    public string SourceKey => SourceKeyValue;

    public int Order => 2;

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

        var accounts = await _accounts.GetAllByClientAsync(new ClientId(request.ClientId), request.OrganizationId, ct);
        var activeAccounts = accounts.Where(account => account.IsActive).ToList();

        if (activeAccounts.Count == 0)
        {
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        var networkIds = activeAccounts.Select(account => account.SocialNetworkId).Distinct().ToList();
        var networks = await _networks.GetByIdsAsync(networkIds, ct);
        var networkCodeById = networks.ToDictionary(network => network.Id, network => network.Code);

        var instagramHandles = activeAccounts
            .Where(account =>
                networkCodeById.TryGetValue(account.SocialNetworkId, out var code)
                && string.Equals(code, InstagramNetwork, StringComparison.OrdinalIgnoreCase))
            .Select(account => account.Handle ?? account.ProfileUrl)
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (instagramHandles.Count == 0)
        {
            // CR-DISC-02 scenario 2: a TikTok-only own client is an honest, non-throwing empty run —
            // never an exception, and never treated as Unconfigured (the route itself is fine; the
            // client simply has no scrapeable account).
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        var found = new List<DiscoveredContent>();

        foreach (var handle in instagramHandles)
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

            // One unreachable account must not empty the whole run.
            if (scrape.IsFailure)
            {
                continue;
            }

            foreach (var post in scrape.Value.OwnerPosts)
            {
                if (post.IsDegraded || post.IsPaidPartnership)
                {
                    continue;
                }

                found.Add(ToDiscovered(post, handle));
            }
        }

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

        var title = string.IsNullOrWhiteSpace(caption)
            ? $"Publicación de {handle}"
            : FirstLine(caption);

        return new DiscoveredContent(
            Title: title,
            Summary: caption ?? string.Empty,
            NetworkSource: InstagramNetwork,
            ContentUrl: $"https://www.instagram.com/p/{post.PostId}/",
            ThumbnailUrl: post.ImageUrls?.FirstOrDefault(),
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
