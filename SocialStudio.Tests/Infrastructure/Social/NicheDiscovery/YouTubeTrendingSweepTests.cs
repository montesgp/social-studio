using System.Net;
using System.Text;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Social.NicheDiscovery.Origins;
using Microsoft.Extensions.Options;

namespace SocialStudio.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// Query-less discovery (<see cref="DiscoveryStage.TrendingSweep"/>). The search stages need a term,
/// which makes them "confirm the niche I already named"; this stage harvests what is already
/// performing without naming anything, which is what the feature promises.
///
/// The provider's only query-less feed is region-addressed, so the sweep samples per region. That is a
/// SOURCING constraint: these tests also pin that no region value escapes into the query the
/// Application layer builds.
/// </summary>
public sealed class YouTubeTrendingSweepTests
{
    private const string BaseAddress = "https://provider.test/v3";
    private static readonly DiscoveryWindow Window = DiscoveryWindow.Create(90).Value;

    [Fact]
    public void TrendingSweep_IsASupportedStage()
    {
        var source = Build(new FakeHttpMessageHandler(_ => Json("{\"items\":[]}")), ["QQ"]);

        Assert.True(source.Supports(DiscoveryStage.TrendingSweep));
    }

    [Fact]
    public async Task Sweep_HarvestsVideos_WithoutEverSendingASearchQuery()
    {
        var handler = new FakeHttpMessageHandler(RespondWithTaxonomyThenChart);
        var source = Build(handler, ["QQ"]);

        var result = await source.DiscoverAsync(Query(), Budget(), CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.NotEmpty(result.Videos);
        Assert.DoesNotContain(handler.RequestedUris, uri => uri.Contains("/search", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.RequestedUris, uri => uri.Contains("q=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sweep_AsksTheProviderForItsMostPopularChart()
    {
        var handler = new FakeHttpMessageHandler(RespondWithTaxonomyThenChart);

        await Build(handler, ["QQ"]).DiscoverAsync(Query(), Budget(), CancellationToken.None);

        Assert.Contains(handler.RequestedUris, uri => uri.Contains("chart=mostPopular", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUris, uri => uri.Contains("videoCategories", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sweep_VisitsEveryConfiguredSamplingRegion()
    {
        var handler = new FakeHttpMessageHandler(RespondWithTaxonomyThenChart);

        await Build(handler, ["QQ", "XY"]).DiscoverAsync(Query(), Budget(), CancellationToken.None);

        Assert.Contains(handler.RequestedUris, uri => uri.Contains("regionCode=QQ", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUris, uri => uri.Contains("regionCode=XY", StringComparison.Ordinal));
    }

    /// <summary>
    /// Charts overlap across categories and regions; the same video must not inflate the corpus, or
    /// every downstream metric counts it twice.
    /// </summary>
    [Fact]
    public async Task Sweep_DeduplicatesVideosSeenInMoreThanOneChart()
    {
        var handler = new FakeHttpMessageHandler(RespondWithTaxonomyThenChart);

        var result = await Build(handler, ["QQ", "XY"]).DiscoverAsync(Query(), Budget(), CancellationToken.None);

        var refs = result.Videos.Select(v => v.ExternalRef).ToList();
        Assert.Equal(refs.Count, refs.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A category with no chart in a given region answers 400. That is an ordinary hole in the
    /// taxonomy — the sweep must keep going rather than abandon the whole region.
    /// </summary>
    [Fact]
    public async Task Sweep_SkipsACategoryTheProviderRejects_AndKeepsSweeping()
    {
        var firstChart = true;
        var handler = new FakeHttpMessageHandler(uri =>
        {
            if (uri.Contains("videoCategories", StringComparison.Ordinal))
            {
                return Json("{\"items\":[{\"id\":\"1\",\"snippet\":{\"assignable\":true}},{\"id\":\"2\",\"snippet\":{\"assignable\":true}}]}");
            }

            if (firstChart)
            {
                firstChart = false;
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
            }

            return Json(ChartPayload("video-kept"));
        });

        var result = await Build(handler, ["QQ"]).DiscoverAsync(Query(), Budget(), CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.Contains(result.Videos, v => v.ExternalRef == "video-kept");
    }

    /// <summary>Budget exhaustion truncates the sweep — a half-swept corpus still ranks.</summary>
    [Fact]
    public async Task Sweep_ExhaustedBudget_ReturnsWhatItAlreadyHarvested_AsPartial()
    {
        var handler = new FakeHttpMessageHandler(RespondWithTaxonomyThenChart);

        var result = await Build(handler, ["QQ", "XY"])
            .DiscoverAsync(Query(), new DiscoveryBudget(3, 1), CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.NotNull(result.Partial);
    }

    [Fact]
    public async Task Sweep_WithNoConfiguredRegions_HarvestsNothing_RatherThanGuessingWhereToLook()
    {
        var handler = new FakeHttpMessageHandler(RespondWithTaxonomyThenChart);

        var result = await Build(handler, []).DiscoverAsync(Query(), Budget(), CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Videos);
        Assert.Empty(handler.RequestedUris);
    }

    /// <summary>
    /// ND-15's promise: region is a sourcing knob, never an analysis dimension. The query the
    /// Application layer hands to a source carries no region at all.
    /// </summary>
    [Fact]
    public void NicheDiscoveryQuery_CarriesNoRegion()
    {
        var propertyNames = typeof(NicheDiscoveryQuery).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(propertyNames, name => name.Contains("Region", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The per-region category cap is the knob that actually saves quota: every category skipped is a
    /// provider call not made. Matters most during development, where a broad corpus buys nothing.
    /// </summary>
    [Fact]
    public async Task Sweep_WithACategoryCap_StopsCallingTheProviderPastTheCap()
    {
        var handler = new FakeHttpMessageHandler(uri =>
            uri.Contains("videoCategories", StringComparison.Ordinal)
                ? Json("{\"items\":[{\"id\":\"1\",\"snippet\":{\"assignable\":true}},"
                    + "{\"id\":\"2\",\"snippet\":{\"assignable\":true}},"
                    + "{\"id\":\"3\",\"snippet\":{\"assignable\":true}}]}")
                : Json(ChartPayload("video-1")));

        await Build(handler, ["QQ"], maxCategoriesPerRegion: 1)
            .DiscoverAsync(Query(), Budget(), CancellationToken.None);

        var chartCalls = handler.RequestedUris.Count(uri => uri.Contains("chart=mostPopular", StringComparison.Ordinal));
        Assert.Equal(1, chartCalls);
    }

    [Fact]
    public async Task Sweep_WithoutACategoryCap_WalksTheWholeTaxonomy()
    {
        var handler = new FakeHttpMessageHandler(uri =>
            uri.Contains("videoCategories", StringComparison.Ordinal)
                ? Json("{\"items\":[{\"id\":\"1\",\"snippet\":{\"assignable\":true}},"
                    + "{\"id\":\"2\",\"snippet\":{\"assignable\":true}},"
                    + "{\"id\":\"3\",\"snippet\":{\"assignable\":true}}]}")
                : Json(ChartPayload("video-1")));

        await Build(handler, ["QQ"]).DiscoverAsync(Query(), Budget(), CancellationToken.None);

        var chartCalls = handler.RequestedUris.Count(uri => uri.Contains("chart=mostPopular", StringComparison.Ordinal));
        Assert.Equal(3, chartCalls);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static HttpResponseMessage RespondWithTaxonomyThenChart(string uri) =>
        uri.Contains("videoCategories", StringComparison.Ordinal)
            ? Json("{\"items\":[{\"id\":\"1\",\"snippet\":{\"assignable\":true}}]}")
            : Json(ChartPayload("video-1"));

    private static string ChartPayload(string videoId) =>
        "{\"items\":[{\"id\":\"" + videoId + "\",\"snippet\":{\"channelId\":\"channel-1\","
        + "\"title\":\"A title\",\"description\":\"A description\","
        + "\"publishedAt\":\"2026-08-01T00:00:00Z\",\"defaultAudioLanguage\":\"es\"},"
        + "\"statistics\":{\"viewCount\":\"10000\",\"likeCount\":\"200\",\"commentCount\":\"30\"},"
        + "\"contentDetails\":{\"duration\":\"PT8M\"}}]}";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static DiscoveryBudget Budget() => new(1_000, 15);

    private static NicheDiscoveryQuery Query() => new(
        Guid.NewGuid(), DiscoveryStage.TrendingSweep, "youtube", "video", "es", Window, [], [], []);

    private static YouTubeDataApiNicheDiscoverySource Build(
        FakeHttpMessageHandler handler, IReadOnlyList<string> samplingRegions, int? maxCategoriesPerRegion = null)
    {
        var options = Options.Create(new NicheDiscoveryOriginOptions
        {
            BaseAddress = BaseAddress,
            SamplingRegions = samplingRegions,
            MaxCategoriesPerRegion = maxCategoriesPerRegion,
        });

        var configuration = new NicheDiscoveryOriginConfiguration(
            new AlwaysEnabledWorkflowResolver(), new StaticApiKeyResolver("test-api-key"), options);

        return new YouTubeDataApiNicheDiscoverySource(
            configuration, new YouTubeDataApiClient(new StaticHttpClientFactory(handler)));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respond;

        public FakeHttpMessageHandler(Func<string, HttpResponseMessage> respond) => _respond = respond;

        public List<string> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!.ToString();
            RequestedUris.Add(uri);
            return Task.FromResult(_respond(uri));
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class AlwaysEnabledWorkflowResolver : IWorkflowConfigurationResolver
    {
        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
                [new WorkflowStepRouteCandidate(
                    ProviderId: Guid.NewGuid(),
                    ProviderKey: "youtube-data-api-v3",
                    OrganizationIntegrationId: Guid.NewGuid(),
                    ConnectionMode: IntegrationConnectionMode.BringYourOwnKey,
                    Priority: 1,
                    ModelId: null,
                    ActorId: null,
                    IsEnabled: true)]));
    }

    private sealed class StaticApiKeyResolver : IIntegrationResolver
    {
        private readonly string _apiKey;

        public StaticApiKeyResolver(string apiKey) => _apiKey = apiKey;

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId, string providerKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new ResolvedIntegration(
                organizationId,
                providerKey,
                "YouTube Data API v3 (test)",
                IntegrationProviderCategory.DataExtraction,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: new Dictionary<string, string> { ["api_key"] = _apiKey })));
    }
}
