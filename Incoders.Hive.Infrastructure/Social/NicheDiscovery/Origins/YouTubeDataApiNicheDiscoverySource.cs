using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Infrastructure.Social.NicheDiscovery.Origins;

/// <summary>
/// The YouTube Data API v3 origin (design §2.4, §5): composes <see cref="NicheDiscoveryOriginConfiguration"/>
/// (routing), <see cref="YouTubeDataApiClient"/> (transport), <see cref="YouTubeQuotaBudget"/> (ceiling)
/// and <see cref="YouTubeEvidenceMapper"/> (normalization). Supports every <see cref="DiscoveryStage"/>:
/// the two search stages run <c>search.list</c> then batch-enrich the harvested ids; the two detail
/// stages enrich the caller-supplied external refs directly, without searching again.
///
/// <see cref="SourceKey"/> is a PROVIDER key, never an actor/endpoint id (ND-03) — distinct from the
/// integrations catalog key ("youtube-data-api-v3") used for credential/workflow routing.
/// </summary>
internal sealed class YouTubeDataApiNicheDiscoverySource : INicheDiscoverySource
{
    internal const string SourceKeyValue = "youtube-data-api";
    private const string NetworkKey = "youtube";
    private const string QuotaExhaustedReasonKey = "social.niche_discovery.provider_quota_exhausted";
    private const string NoEvidenceMappedReasonKey = "social.niche_discovery.no_evidence_mapped";

    private readonly NicheDiscoveryOriginConfiguration _configuration;
    private readonly YouTubeDataApiClient _client;

    internal YouTubeDataApiNicheDiscoverySource(
        NicheDiscoveryOriginConfiguration configuration,
        YouTubeDataApiClient client,
        int order = 1)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(client);

        _configuration = configuration;
        _client = client;
        Order = order;
    }

    public string SourceKey => SourceKeyValue;

    public int Order { get; }

    public bool Supports(DiscoveryStage stage) => stage is
        DiscoveryStage.ChannelSearch or DiscoveryStage.VideoSearch or DiscoveryStage.ChannelDetail
        or DiscoveryStage.VideoDetail or DiscoveryStage.TrendingSweep;

    public async Task<NicheDiscoverySourceResult> DiscoverAsync(
        NicheDiscoveryQuery query, DiscoveryBudget budget, CancellationToken ct = default)
    {
        var route = await _configuration.ResolveAsync(query.OrganizationId, query.Stage, NetworkKey, ct);
        if (!route.IsConfigured)
        {
            return NicheDiscoverySourceResult.Unconfigured();
        }

        var ledger = new YouTubeQuotaBudget(budget);

        return query.Stage switch
        {
            DiscoveryStage.ChannelSearch => await SearchAsync(query, route, ledger, harvestChannels: true, ct),
            DiscoveryStage.VideoSearch => await SearchAsync(query, route, ledger, harvestChannels: false, ct),
            DiscoveryStage.ChannelDetail => await EnrichChannelsAsync(query.ChannelExternalRefs, route, ledger, ct),
            DiscoveryStage.VideoDetail => await EnrichVideosAsync(query.VideoExternalRefs, route, ledger, ct),
            DiscoveryStage.TrendingSweep => await SweepTrendingAsync(route, ledger, ct),
            _ => NicheDiscoverySourceResult.Unconfigured(),
        };
    }

    private async Task<NicheDiscoverySourceResult> SearchAsync(
        NicheDiscoveryQuery query,
        NicheDiscoveryOriginRoute route,
        YouTubeQuotaBudget ledger,
        bool harvestChannels,
        CancellationToken ct)
    {
        var harvestedIds = new List<string>();
        var providerUnitsSpent = 0;

        foreach (var term in query.SeedTerms)
        {
            if (!ledger.CanAfford(YouTubeDataApiClient.SearchUnitCost))
            {
                return FinishPartial(harvestedIds.Count == 0, providerUnitsSpent, PartialReason.BudgetExhausted);
            }

            ledger.Spend(YouTubeDataApiClient.SearchUnitCost);
            providerUnitsSpent += YouTubeDataApiClient.SearchUnitCost;

            var searchResult = await _client.SearchVideosAsync(
                route.BaseAddress!,
                route.ApiKey!,
                term,
                query.TargetMarketLanguage,
                DateTimeOffset.UtcNow.AddDays(-query.Window.Days),
                50,
                ct);

            if (!searchResult.IsSuccess)
            {
                if (searchResult.IsQuotaExceeded)
                {
                    return FinishPartial(harvestedIds.Count == 0, providerUnitsSpent, PartialReason.ProviderBudgetExhausted, isQuotaFailure: true);
                }

                continue;
            }

            harvestedIds.AddRange(ExtractSearchIds(searchResult.Payload!.Value, harvestChannels));
        }

        var distinctIds = harvestedIds.Distinct(StringComparer.Ordinal).ToList();
        if (distinctIds.Count == 0)
        {
            return NicheDiscoverySourceResult.Ran([], [], new BudgetSpend(providerUnitsSpent, 0));
        }

        return harvestChannels
            ? await EnrichChannelsAsync(distinctIds, route, ledger, ct, providerUnitsSpent)
            : await EnrichVideosAsync(distinctIds, route, ledger, ct, providerUnitsSpent);
    }

    /// <summary>
    /// Query-less sweep (<see cref="DiscoveryStage.TrendingSweep"/>): for each sampling region, walk the
    /// region's category taxonomy and pull the most-popular chart per category. Nothing here names a
    /// niche — the niches are whatever the semantic annotator later finds in the harvested corpus.
    ///
    /// Videos arrive fully enriched (<c>snippet,statistics,contentDetails</c>), so no detail pass is
    /// needed; the owning channels still are, and the processor's ChannelDetail stage picks them up
    /// from the video refs exactly as it already does for the search path.
    ///
    /// Every call is a bounded <see cref="YouTubeDataApiClient.ListUnitCost"/>, so budget exhaustion
    /// truncates the sweep into a PARTIAL result rather than failing it: a half-swept corpus still
    /// ranks, and the run is marked degraded upstream.
    /// </summary>
    private async Task<NicheDiscoverySourceResult> SweepTrendingAsync(
        NicheDiscoveryOriginRoute route,
        YouTubeQuotaBudget ledger,
        CancellationToken ct)
    {
        if (route.Regions.Count == 0)
        {
            return NicheDiscoverySourceResult.Ran([], [], new BudgetSpend(0, 0));
        }

        var videos = new List<VideoEvidence>();
        var providerUnitsSpent = 0;
        var totalItems = 0;

        foreach (var region in route.Regions)
        {
            if (!ledger.CanAfford(YouTubeDataApiClient.ListUnitCost))
            {
                return FinishSweep(videos, providerUnitsSpent, totalItems, truncated: true);
            }

            ledger.Spend(YouTubeDataApiClient.ListUnitCost);
            providerUnitsSpent += YouTubeDataApiClient.ListUnitCost;

            var categories = await _client.ListVideoCategoriesAsync(route.BaseAddress!, route.ApiKey!, region, ct);
            if (!categories.IsSuccess)
            {
                if (categories.IsQuotaExceeded)
                {
                    return FinishSweep(videos, providerUnitsSpent, totalItems, truncated: true, isQuotaFailure: true);
                }

                continue;
            }

            // A region with no usable taxonomy still gets one untargeted chart pull rather than being
            // skipped outright — the chart is defined with or without a category filter.
            var categoryIds = route.CapCategories(ExtractCategoryIds(categories.Payload!.Value));
            var targets = categoryIds.Count > 0 ? categoryIds : [null];

            foreach (var categoryId in targets)
            {
                if (!ledger.CanAfford(YouTubeDataApiClient.ListUnitCost))
                {
                    return FinishSweep(videos, providerUnitsSpent, totalItems, truncated: true);
                }

                ledger.Spend(YouTubeDataApiClient.ListUnitCost);
                providerUnitsSpent += YouTubeDataApiClient.ListUnitCost;

                var chart = await _client.ListTrendingVideosAsync(
                    route.BaseAddress!, route.ApiKey!, region, categoryId, YouTubeDataApiClient.MaxIdsPerBatch, ct);

                if (!chart.IsSuccess)
                {
                    if (chart.IsQuotaExceeded)
                    {
                        return FinishSweep(videos, providerUnitsSpent, totalItems, truncated: true, isQuotaFailure: true);
                    }

                    // Not every category carries a chart in every region; the provider answers 400.
                    // That is an ordinary hole in the taxonomy, not a run failure.
                    continue;
                }

                var mapped = YouTubeEvidenceMapper.MapVideos(chart.Payload!.Value, SourceKeyValue);
                totalItems += mapped.TotalItems;
                videos.AddRange(mapped.Videos);
            }
        }

        return FinishSweep(videos, providerUnitsSpent, totalItems, truncated: false);
    }

    private static NicheDiscoverySourceResult FinishSweep(
        List<VideoEvidence> videos, int providerUnitsSpent, int totalItems, bool truncated, bool isQuotaFailure = false)
    {
        var deduped = videos
            .GroupBy(v => v.ExternalRef, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (deduped.Count == 0)
        {
            if (isQuotaFailure)
            {
                return NicheDiscoverySourceResult.Failed(QuotaExhaustedReasonKey, new BudgetSpend(providerUnitsSpent, 0));
            }

            return totalItems > 0
                ? NicheDiscoverySourceResult.Failed(NoEvidenceMappedReasonKey, new BudgetSpend(providerUnitsSpent, 0))
                : NicheDiscoverySourceResult.Ran([], [], new BudgetSpend(providerUnitsSpent, 0));
        }

        return truncated
            ? NicheDiscoverySourceResult.Ran(
                [], deduped, new BudgetSpend(providerUnitsSpent, 0),
                isQuotaFailure ? PartialReason.ProviderBudgetExhausted : PartialReason.BudgetExhausted)
            : NicheDiscoverySourceResult.Ran([], deduped, new BudgetSpend(providerUnitsSpent, 0));
    }

    private static IReadOnlyList<string?> ExtractCategoryIds(JsonElement payload)
    {
        if (!payload.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<string?>();

        foreach (var item in items.EnumerateArray())
        {
            // `assignable: false` categories accept no uploads, so their chart is always empty.
            if (item.TryGetProperty("snippet", out var snippet)
                && snippet.TryGetProperty("assignable", out var assignable)
                && assignable.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                ids.Add(id.GetString());
            }
        }

        return ids;
    }

    private async Task<NicheDiscoverySourceResult> EnrichVideosAsync(
        IReadOnlyList<string> videoExternalRefs,
        NicheDiscoveryOriginRoute route,
        YouTubeQuotaBudget ledger,
        CancellationToken ct,
        int providerUnitsAlreadySpent = 0)
    {
        if (videoExternalRefs.Count == 0)
        {
            return NicheDiscoverySourceResult.Ran([], [], new BudgetSpend(providerUnitsAlreadySpent, 0));
        }

        var videos = new List<VideoEvidence>();
        var providerUnitsSpent = providerUnitsAlreadySpent;
        var totalItems = 0;

        foreach (var batch in Batch(videoExternalRefs, YouTubeDataApiClient.MaxIdsPerBatch))
        {
            if (!ledger.CanAfford(YouTubeDataApiClient.ListUnitCost))
            {
                return NicheDiscoverySourceResult.Ran([], videos, new BudgetSpend(providerUnitsSpent, 0), PartialReason.BudgetExhausted);
            }

            ledger.Spend(YouTubeDataApiClient.ListUnitCost);
            providerUnitsSpent += YouTubeDataApiClient.ListUnitCost;

            var detail = await _client.ListVideosAsync(route.BaseAddress!, route.ApiKey!, batch, ct);
            if (!detail.IsSuccess)
            {
                if (detail.IsQuotaExceeded)
                {
                    return videos.Count > 0
                        ? NicheDiscoverySourceResult.Ran([], videos, new BudgetSpend(providerUnitsSpent, 0), PartialReason.ProviderBudgetExhausted)
                        : NicheDiscoverySourceResult.Failed(QuotaExhaustedReasonKey, new BudgetSpend(providerUnitsSpent, 0));
                }

                continue;
            }

            var mapped = YouTubeEvidenceMapper.MapVideos(detail.Payload!.Value, SourceKeyValue);
            totalItems += mapped.TotalItems;
            videos.AddRange(mapped.Videos);
        }

        if (totalItems > 0 && videos.Count == 0)
        {
            return NicheDiscoverySourceResult.Failed(NoEvidenceMappedReasonKey, new BudgetSpend(providerUnitsSpent, 0));
        }

        return NicheDiscoverySourceResult.Ran([], videos, new BudgetSpend(providerUnitsSpent, 0));
    }

    private async Task<NicheDiscoverySourceResult> EnrichChannelsAsync(
        IReadOnlyList<string> channelExternalRefs,
        NicheDiscoveryOriginRoute route,
        YouTubeQuotaBudget ledger,
        CancellationToken ct,
        int providerUnitsAlreadySpent = 0)
    {
        if (channelExternalRefs.Count == 0)
        {
            return NicheDiscoverySourceResult.Ran([], [], new BudgetSpend(providerUnitsAlreadySpent, 0));
        }

        var channels = new List<ChannelEvidence>();
        var providerUnitsSpent = providerUnitsAlreadySpent;
        var totalItems = 0;

        foreach (var batch in Batch(channelExternalRefs, YouTubeDataApiClient.MaxIdsPerBatch))
        {
            if (!ledger.CanAfford(YouTubeDataApiClient.ListUnitCost))
            {
                return NicheDiscoverySourceResult.Ran(channels, [], new BudgetSpend(providerUnitsSpent, 0), PartialReason.BudgetExhausted);
            }

            ledger.Spend(YouTubeDataApiClient.ListUnitCost);
            providerUnitsSpent += YouTubeDataApiClient.ListUnitCost;

            var detail = await _client.ListChannelsAsync(route.BaseAddress!, route.ApiKey!, batch, ct);
            if (!detail.IsSuccess)
            {
                if (detail.IsQuotaExceeded)
                {
                    return channels.Count > 0
                        ? NicheDiscoverySourceResult.Ran(channels, [], new BudgetSpend(providerUnitsSpent, 0), PartialReason.ProviderBudgetExhausted)
                        : NicheDiscoverySourceResult.Failed(QuotaExhaustedReasonKey, new BudgetSpend(providerUnitsSpent, 0));
                }

                continue;
            }

            var mapped = YouTubeEvidenceMapper.MapChannels(detail.Payload!.Value, SourceKeyValue);
            totalItems += mapped.TotalItems;
            channels.AddRange(mapped.Channels);
        }

        if (totalItems > 0 && channels.Count == 0)
        {
            return NicheDiscoverySourceResult.Failed(NoEvidenceMappedReasonKey, new BudgetSpend(providerUnitsSpent, 0));
        }

        return NicheDiscoverySourceResult.Ran(channels, [], new BudgetSpend(providerUnitsSpent, 0));
    }

    private static NicheDiscoverySourceResult FinishPartial(
        bool hasNoEvidenceYet, int providerUnitsSpent, PartialReason reason, bool isQuotaFailure = false)
    {
        if (isQuotaFailure && hasNoEvidenceYet)
        {
            return NicheDiscoverySourceResult.Failed(QuotaExhaustedReasonKey, new BudgetSpend(providerUnitsSpent, 0));
        }

        return NicheDiscoverySourceResult.Ran([], [], new BudgetSpend(providerUnitsSpent, 0), reason);
    }

    private static IEnumerable<IReadOnlyList<string>> Batch(IReadOnlyList<string> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }

    /// <summary>
    /// search.list (type=video) items carry BOTH <c>id.videoId</c> and <c>snippet.channelId</c> in the
    /// same response — the ~1-2% detail-cost strategy of design §5.3.
    /// </summary>
    private static IReadOnlyList<string> ExtractSearchIds(JsonElement searchResponse, bool harvestChannels)
    {
        var ids = new List<string>();

        if (!searchResponse.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (harvestChannels)
            {
                if (item.TryGetProperty("snippet", out var snippet)
                    && snippet.TryGetProperty("channelId", out var channelId)
                    && channelId.ValueKind == JsonValueKind.String)
                {
                    var value = channelId.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        ids.Add(value);
                    }
                }
            }
            else if (item.TryGetProperty("id", out var idElement)
                && idElement.TryGetProperty("videoId", out var videoId)
                && videoId.ValueKind == JsonValueKind.String)
            {
                var value = videoId.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    ids.Add(value);
                }
            }
        }

        return ids;
    }
}
