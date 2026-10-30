using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Read-through cache over <see cref="IAccountBaselineRepository"/>, backed by the SAME
/// <see cref="IProfileScrapeResolver"/> + <see cref="IProfileMetricCalculator"/> pair the profile
/// analyzer already uses (design §6.5).
///
/// This is the ONLY thing standing between "known account + TOP-N baselines" and a per-candidate
/// scrape bill: the call site must invoke this for reference/own account handles (already scraped for
/// free elsewhere) and TOP-N candidate authors after cheap filtering — NEVER for the whole raw
/// candidate set. The provider itself enforces nothing about which handles it is called for; it only
/// guarantees that calling it again for the same author within the TTL costs zero extra scrapes.
/// </summary>
public sealed class AccountBaselineProvider
{
    /// <summary>Assumed product value (design §0): 30-day baseline staleness window.</summary>
    private static readonly TimeSpan StalenessWindow = TimeSpan.FromDays(30);

    private readonly IAccountBaselineRepository _repository;
    private readonly IProfileScrapeResolver _scraper;
    private readonly IProfileMetricCalculator _calculator;

    public AccountBaselineProvider(
        IAccountBaselineRepository repository,
        IProfileScrapeResolver scraper,
        IProfileMetricCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(scraper);
        ArgumentNullException.ThrowIfNull(calculator);

        _repository = repository;
        _scraper = scraper;
        _calculator = calculator;
    }

    /// <summary>
    /// Returns the fresh cached baseline (no scrape), recomputes a stale one (exactly one scrape), or
    /// returns null when nothing is cached and the scrape/compute fails — NEVER a fabricated baseline
    /// (CD-07, CD-09).
    /// </summary>
    public async Task<AccountBaseline?> GetOrComputeAsync(
        Guid organizationId,
        string networkKey,
        string authorHandle,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(networkKey);
        ArgumentNullException.ThrowIfNull(authorHandle);

        var cached = await _repository.GetAsync(organizationId, networkKey, authorHandle, ct);

        if (cached is not null && cached.ComputedAt >= nowUtc - StalenessWindow)
        {
            // CD-09 scenario 1: fresh baseline read without a new scrape.
            return cached;
        }

        var scrape = await _scraper.ResolveAsync(
            new ProfileScrapeRequest(
                OrganizationId: organizationId,
                AnalysisId: Guid.Empty,
                ClientId: null,
                ProfileUrlOrHandle: authorHandle,
                PlatformKey: networkKey,
                CapabilityKey: "profile"),
            ct);

        if (scrape.IsFailure)
        {
            // A provider outage must not fabricate a baseline; the stale cached value (if any) is the
            // most honest thing this call can return.
            return cached;
        }

        var computed = _calculator.Compute(scrape.Value.OwnerPosts, scrape.Value.Profile);

        if (computed.IsFailure)
        {
            return cached;
        }

        var baseline = new AccountBaseline(
            NetworkKey: networkKey,
            AuthorHandle: authorHandle,
            MedianViews: computed.Value.MedianViews,
            MedianEngagement: computed.Value.EngagementRate,
            PostCount: computed.Value.OwnerPostCount,
            ComputedAt: nowUtc);

        await _repository.UpsertAsync(organizationId, baseline, ct);

        return baseline;
    }
}
