using System.Net;
using System.Text;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery.Origins;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// WU-5 (ND-02, ND-03, N9): <c>Supports(stage)</c> per <see cref="DiscoveryStage"/>; <c>DiscoverAsync</c>
/// composes configuration + client + budget + mapper; <c>SourceKey</c> is a PROVIDER key, never an
/// actor/endpoint literal. The 403 quotaExceeded mapping (N9) is exercised here end-to-end against a
/// scripted fake handler, per the task's "counting fake HTTP handler" instruction.
/// </summary>
public sealed class YouTubeDataApiNicheDiscoverySourceTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Theory]
    [InlineData(DiscoveryStage.ChannelSearch)]
    [InlineData(DiscoveryStage.VideoSearch)]
    [InlineData(DiscoveryStage.ChannelDetail)]
    [InlineData(DiscoveryStage.VideoDetail)]
    public void Supports_EveryDiscoveryStage(DiscoveryStage stage)
    {
        var source = BuildSource(new ScriptedHandler());

        Assert.True(source.Supports(stage));
    }

    [Fact]
    public void SourceKey_IsAProviderKey_NeverAnActorOrEndpointLiteral()
    {
        var source = BuildSource(new ScriptedHandler());

        Assert.Equal("youtube-data-api", source.SourceKey);
    }

    [Fact]
    public async Task DiscoverAsync_UnconfiguredRoute_ReturnsUnconfigured_NeverCallsTheClient()
    {
        var handler = new ScriptedHandler();
        var source = BuildSource(handler, routeIsConfigured: false);

        var result = await source.DiscoverAsync(
            MakeQuery(DiscoveryStage.VideoDetail, videoRefs: ["vid-1"]),
            DiscoveryBudget.ForDepth(DiscoveryDepth.Quick),
            CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Unconfigured, result.Outcome);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DiscoverAsync_VideoDetail_ComposesClientAndMapper_ReturningMappedEvidence()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, SingleVideoResponseJson);

        var source = BuildSource(handler);

        var result = await source.DiscoverAsync(
            MakeQuery(DiscoveryStage.VideoDetail, videoRefs: ["vid-001"]),
            DiscoveryBudget.ForDepth(DiscoveryDepth.Quick),
            CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.Single(result.Videos);
        Assert.Equal("vid-001", result.Videos[0].ExternalRef);
        Assert.Equal("youtube-data-api", result.Videos[0].SourceKey);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, result.Spend.ProviderUnits);
    }

    [Fact]
    public async Task DiscoverAsync_QuotaExceeded_WithZeroResults_MapsToFailed()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, QuotaExceededResponseJson);

        var source = BuildSource(handler);

        var result = await source.DiscoverAsync(
            MakeQuery(DiscoveryStage.VideoDetail, videoRefs: ["vid-001"]),
            DiscoveryBudget.ForDepth(DiscoveryDepth.Quick),
            CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Failed, result.Outcome);
        Assert.Equal("social.niche_discovery.provider_quota_exhausted", result.FailureReasonKey);
    }

    [Fact]
    public async Task DiscoverAsync_QuotaExceeded_WithPartialResultsAlready_MapsToRan_WithProviderBudgetExhausted()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, SingleVideoResponseJson);
        handler.Enqueue(HttpStatusCode.Forbidden, QuotaExceededResponseJson);

        var source = BuildSource(handler);

        var manyIds = Enumerable.Range(1, 100).Select(i => $"vid-{i}").ToList();
        var result = await source.DiscoverAsync(
            MakeQuery(DiscoveryStage.VideoDetail, videoRefs: manyIds),
            DiscoveryBudget.ForDepth(DiscoveryDepth.Quick),
            CancellationToken.None);

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.Equal(PartialReason.ProviderBudgetExhausted, result.Partial);
        Assert.Single(result.Videos);
    }

    private static NicheDiscoveryQuery MakeQuery(
        DiscoveryStage stage,
        IReadOnlyList<string>? videoRefs = null,
        IReadOnlyList<string>? channelRefs = null,
        IReadOnlyList<string>? seedTerms = null) =>
        new(
            OrganizationId,
            stage,
            "youtube",
            "shorts",
            "es",
            DiscoveryWindow.Default(),
            seedTerms ?? ["niche term"],
            channelRefs ?? [],
            videoRefs ?? []);

    private static YouTubeDataApiNicheDiscoverySource BuildSource(ScriptedHandler handler, bool routeIsConfigured = true)
    {
        var options = Options.Create(new NicheDiscoveryOriginOptions { BaseAddress = "https://www.googleapis.com/youtube/v3" });
        var configuration = new NicheDiscoveryOriginConfiguration(
            new FakeWorkflowConfigurationResolver(routeIsConfigured),
            new FakeIntegrationResolver(routeIsConfigured ? "test-api-key" : null),
            options);

        var client = new YouTubeDataApiClient(new StaticHttpClientFactory(handler));

        return new YouTubeDataApiNicheDiscoverySource(configuration, client);
    }

    private const string SingleVideoResponseJson = """
    {
      "items": [
        {
          "id": "vid-001",
          "snippet": { "channelId": "chan-1", "title": "Mapped video", "publishedAt": "2026-01-01T00:00:00Z" },
          "statistics": { "viewCount": "100" },
          "contentDetails": { "duration": "PT1M0S" }
        }
      ]
    }
    """;

    private const string QuotaExceededResponseJson = """
    {
      "error": {
        "code": 403,
        "errors": [{ "reason": "quotaExceeded", "message": "The request cannot be completed because you have exceeded your quota." }]
      }
    }
    """;

    private sealed class FakeWorkflowConfigurationResolver : IWorkflowConfigurationResolver
    {
        private readonly bool _enabled;

        public FakeWorkflowConfigurationResolver(bool enabled)
        {
            _enabled = enabled;
        }

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WorkflowStepRouteCandidate> candidates = _enabled
                ?
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(), "youtube-data-api-v3", Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                        ModelId: null, ActorId: null, IsEnabled: true),
                ]
                : [];

            return Task.FromResult(Result.Success(candidates));
        }
    }

    private sealed class FakeIntegrationResolver : IIntegrationResolver
    {
        private readonly string? _apiKey;

        public FakeIntegrationResolver(string? apiKey)
        {
            _apiKey = apiKey;
        }

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default)
        {
            var credentials = new Dictionary<string, string>();
            if (_apiKey is not null)
            {
                credentials["api_key"] = _apiKey;
            }

            return Task.FromResult(Result.Success(new ResolvedIntegration(
                organizationId,
                providerKey,
                "YouTube Data API v3 (test)",
                IntegrationProviderCategory.OfficialDataApi,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: credentials)));
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public int CallCount { get; private set; }

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;

            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json"),
                });
            }

            var (status, body) = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StaticHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
