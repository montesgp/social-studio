using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Ideas;

/// <summary>
/// Mines the client's referent accounts for the posts that actually performed, and turns those into
/// topics.
///
/// This is the first source grounded in EVIDENCE rather than in what we or a model believe: the ideas
/// carry a real audience signal, which is what lets the chain keep the winners when a batch has to be
/// trimmed. It is also the closest thing built so far to the end goal of pulling winning content in
/// from the market — the difference being that this looks at accounts the client already named,
/// rather than searching a niche.
/// </summary>
public sealed class ReferenceAccountIdeaSource : IContentIdeaSource
{
    public const string Key = "reference-accounts";

    /// <summary>
    /// Weights used when a post carries no view count. Images and carousels never report views, and
    /// scoring them zero would bury every non-video winner beneath the weakest reel. Comments are
    /// worth more than likes because they cost the viewer more effort.
    /// </summary>
    private const double LikeWeight = 1d;
    private const double CommentWeight = 8d;

    private readonly IClientBrandSnapshotLoader _snapshots;
    private readonly IProfileScrapeResolver _scraper;

    public ReferenceAccountIdeaSource(
        IClientBrandSnapshotLoader snapshots,
        IProfileScrapeResolver scraper)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(scraper);

        _snapshots = snapshots;
        _scraper = scraper;
    }

    public string SourceKey => Key;

    public int Order => 2;

    /// <summary>Observed: every topic here is lifted from a post that really exists and really performed.</summary>
    public IdeaOrigin Origin => IdeaOrigin.Observed;

    public async Task<IReadOnlyList<ContentIdea>> DrawAsync(ContentIdeaRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Count <= 0)
        {
            return [];
        }

        var snapshot = await _snapshots.LoadAsync(request.OrganizationId, request.ClientId, ct);

        var referents = snapshot.ReferentAccounts
            .Select(account => account.Handle ?? account.ProfileUrl)
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // No references configured — nothing to mine. The chain moves on.
        if (referents.Count == 0)
        {
            return [];
        }

        var candidates = new List<(ProfilePost Post, string Handle, double Score)>();
        var failures = 0;

        foreach (var handle in referents)
        {
            ct.ThrowIfCancellationRequested();

            var scrape = await _scraper.ResolveAsync(
                new ProfileScrapeRequest(
                    OrganizationId: request.OrganizationId,
                    // No analysis backs an idea draw; the id only correlates provider diagnostics.
                    AnalysisId: Guid.Empty,
                    ClientId: request.ClientId,
                    ProfileUrlOrHandle: handle,
                    PlatformKey: "instagram",
                    CapabilityKey: "profile"),
                ct);

            if (scrape.IsFailure)
            {
                // One dead account must not cost the operator every other reference.
                failures++;
                continue;
            }

            foreach (var post in scrape.Value.OwnerPosts)
            {
                if (!IsUsable(post))
                {
                    continue;
                }

                candidates.Add((post, handle, Score(post)));
            }
        }

        // Every reference failed: that is a provider problem, and reporting it as "no ideas" would
        // read as "this client has no references". Throwing lets the chain record the failure.
        if (failures == referents.Count)
        {
            throw new InvalidOperationException(
                $"Idea source '{Key}' could not scrape any of the {referents.Count} reference account(s).");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ideas = new List<ContentIdea>(Math.Min(candidates.Count, request.Count));

        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            if (ideas.Count >= request.Count)
            {
                break;
            }

            var title = Topic(candidate.Post.Caption!);

            if (!seen.Add(title))
            {
                continue;
            }

            ideas.Add(new ContentIdea(
                Title: title,
                Summary: candidate.Post.Caption!.Trim(),
                SourceKey: Key,
                Reference: candidate.Handle,
                Signal: candidate.Score));
        }

        return ideas;
    }

    private static bool IsUsable(ProfilePost post)
    {
        // A caption-less post gives nothing to write about, however well it performed.
        if (string.IsNullOrWhiteSpace(post.Caption) || post.IsDegraded)
        {
            return false;
        }

        // A sponsored post performs because it was paid to. Copying it teaches the wrong lesson about
        // what this audience organically responds to.
        return !post.IsPaidPartnership;
    }

    private static double Score(ProfilePost post) =>
        post.VideoViewCount is { } views && views > 0
            ? views
            : (post.LikesCount * LikeWeight) + (post.CommentsCount * CommentWeight);

    /// <summary>
    /// Reduces a caption to a workable topic line. Captions run long and carry hashtag tails; the
    /// generator needs the subject, and the full text still travels in the summary.
    /// </summary>
    private static string Topic(string caption)
    {
        var firstLine = caption
            .Trim()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim() ?? caption.Trim();

        const int maxLength = 160;

        return firstLine.Length <= maxLength
            ? firstLine
            : firstLine[..maxLength].TrimEnd();
    }
}
