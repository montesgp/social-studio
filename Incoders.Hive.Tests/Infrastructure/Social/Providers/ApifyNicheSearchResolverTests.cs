using System.Net;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Social.Providers;

/// <summary>
/// CD-12 scenario 1 (configured route resolves and the source runs, actor id read from configuration,
/// never a literal) and CD-13 (no enabled route → Unconfigured, never a throw, never a substituted
/// literal). Same fake-handler pattern as <c>ApifyProfileScrapeResolverTests</c> — the real Apify API is
/// never called.
/// </summary>
public sealed class ApifyNicheSearchResolverTests
{
    private static readonly string FixtureJson =
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Infrastructure", "Social", "Fixtures",
                "tiktok-niche-search.json"));

    private static readonly string ActorMappingsJson = JsonSerializer.Serialize(new[]
    {
        new { networkKey = "tiktok", variantKey = "niche-search", actorId = "fallback-mapping-actor", isPrimary = true },
    });

    private static ResolvedProviderCredential MakeCredential(string? mappingsJson = null, string apiKey = "test-key") =>
        new(
            Guid.NewGuid(),
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>
            {
                ["api_key"] = apiKey,
                ["actor_mappings_json"] = mappingsJson ?? ActorMappingsJson,
            },
            SelectedModel: null);

    private static NicheSearchRequest MakeRequest(Guid orgId) =>
        new(orgId, "tiktok", "content strategy", MaxResults: 20);

    [Fact]
    public async Task SearchAsync_ConfiguredWorkflowStepRoute_UsesTheConfiguredActorId_NeverALiteral()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
            [
                new WorkflowStepRouteCandidate(
                    Guid.NewGuid(), "apify", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                    ModelId: null, ActorId: "configured-workflow-actor", IsEnabled: true),
            ]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.NotEmpty(result.Candidates);
        Assert.Equal("configured-workflow-actor", handler.LastActorIdInUrl);
        Assert.All(result.Candidates, c => Assert.Equal("niche", c.SourceKey));
    }

    [Fact]
    public async Task SearchAsync_NoEnabledWorkflowRoute_FallsBackToActorMappingsJson()
    {
        // No enabled workflow-step route at all — resolves via the raw actor_mappings_json entry,
        // exactly like ApifyProfileScrapeResolver.cs:146-151 (design §8.1).
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(FixtureJson);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Equal("fallback-mapping-actor", handler.LastActorIdInUrl);
    }

    [Fact]
    public async Task SearchAsync_NoRouteAndNoMapping_ReturnsUnconfigured_NeverThrows_NeverSubstitutesALiteral()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(mappingsJson: "[]"),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Unconfigured, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_UnsupportedNetwork_ReturnsUnconfigured_NeverCrashes()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
            [
                new WorkflowStepRouteCandidate(
                    Guid.NewGuid(), "apify", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                    ModelId: null, ActorId: "configured-workflow-actor", IsEnabled: true),
            ]));

        var handler = new ProgrammableHandler();

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(
            new NicheSearchRequest(Guid.NewGuid(), "instagram", "content strategy", 20),
            CancellationToken.None);

        Assert.Equal(SourceOutcome.Unconfigured, result.Outcome);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_ZeroOfManyMapped_ReturnsFailed()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var unmappableItems = JsonSerializer.Serialize(new[]
        {
            new { text = "no url one" },
            new { text = "no url two" },
        });

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess(unmappableItems);

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Failed, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task SearchAsync_EmptyResponse_IsALegitimateSuccessWithZeroResults()
    {
        var workflowResolver = new FakeWorkflowConfigurationResolver(
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));

        var handler = new ProgrammableHandler();
        handler.EnqueueSuccess("[]");

        var resolver = new ApifyNicheSearchResolver(
            workflowResolver,
            MakeCredential(),
            new StaticHttpClientFactory(handler));

        var result = await resolver.SearchAsync(MakeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class FakeWorkflowConfigurationResolver : IWorkflowConfigurationResolver
    {
        private readonly Result<IReadOnlyList<WorkflowStepRouteCandidate>> _result;

        public FakeWorkflowConfigurationResolver(Result<IReadOnlyList<WorkflowStepRouteCandidate>> result)
        {
            _result = result;
        }

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class ProgrammableHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public int CallCount { get; private set; }
        public string? LastActorIdInUrl { get; private set; }

        public void EnqueueSuccess(string body)
        {
            _responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;

            // https://api.apify.com/v2/acts/{actorId}/run-sync-get-dataset-items?token=...
            var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var actsIndex = Array.IndexOf(segments, "acts");
            LastActorIdInUrl = actsIndex >= 0 && actsIndex + 1 < segments.Length ? segments[actsIndex + 1] : null;

            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(_responses.Dequeue()());
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
