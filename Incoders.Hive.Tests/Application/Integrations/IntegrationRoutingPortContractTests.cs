using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Application.Integrations;

public sealed class IntegrationRoutingPortContractTests
{
    [Fact]
    public async Task OrganizationIntegrationResolverContract_ReturnsOrderedCandidatesWithoutCredentials()
    {
        var organizationId = Guid.NewGuid();
        var candidates = new[]
        {
            Candidate("openai", priority: 20),
            Candidate("claude", priority: 10)
        };
        IOrganizationIntegrationResolver resolver = new FakeOrganizationIntegrationResolver(candidates);

        var result = await resolver.ResolveRouteAsync(
            new IntegrationRouteRequest(
                organizationId,
                "single-analyzer",
                "ai-analysis",
                PlatformKey: null,
                PreferredProviderKey: null,
                PreferredModel: "claude-3-5-sonnet"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["claude", "openai"], result.Value.Candidates.Select(x => x.ProviderKey).ToArray());
        Assert.DoesNotContain(result.Value.Candidates, x => x.GetType().GetProperties().Any(p => p.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task CredentialResolverContract_DecryptsAtBoundaryForSelectedCandidateOnly()
    {
        var candidate = Candidate("openai", priority: 10);
        IIntegrationCredentialResolver resolver = new FakeCredentialResolver(candidate.ProviderKey);

        var result = await resolver.ResolveAsync(Guid.NewGuid(), candidate, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("openai", result.Value.ProviderKey);
        Assert.Equal("sk-test", result.Value.Credentials["api_key"]);
    }

    [Fact]
    public async Task AiGatewayContract_ReturnsStructuredJsonCompatiblePayload()
    {
        IAiCompletionGateway gateway = new FakeAiCompletionGateway();

        var result = await gateway.CompleteJsonAsync<TestAnalysisPayload>(
            new AiJsonCompletionRequest(
                OrganizationId: Guid.NewGuid(),
                AnalysisId: null,
                CapabilityKey: "ai-analysis",
                PromptVersionKey: "single-analyzer-v1",
                InputJson: "{\"summary\":\"input\"}",
                PreferredProviderKey: "openai",
                PreferredModel: "gpt-4.1-mini"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("output", result.Value.Payload.Summary);
        Assert.Equal("openai", result.Value.ProviderKey);
    }

    [Fact]
    public async Task SocialExtractorContract_RepresentsPartialSnapshotsWithWarnings()
    {
        ISocialContentExtractor extractor = new FakeSocialContentExtractor();

        var result = await extractor.ExtractAsync(
            new SocialContentRequest(
                OrganizationId: Guid.NewGuid(),
                SourceUrl: "https://www.tiktok.com/@brand/video/123",
                NormalizedUrl: "https://www.tiktok.com/@brand/video/123",
                PlatformKey: "tiktok",
                CapabilityKey: "social-extraction"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("tiktok", result.Value.PlatformKey);
        Assert.Contains(result.Value.Warnings, warning => warning.Code == "transcript_missing");
    }

    private static IntegrationRouteCandidate Candidate(string providerKey, int priority) =>
        IntegrationRouteCandidate.Create(
            Guid.NewGuid(),
            providerKey,
            IntegrationConnectionMode.BringYourOwnKey,
            priority,
            allowManagedFallback: false,
            maxAttempts: 1,
            timeoutMs: 2_000,
            selectedModel: null,
            isEnabled: true).Value;

    private sealed class FakeOrganizationIntegrationResolver : IOrganizationIntegrationResolver
    {
        private readonly IReadOnlyList<IntegrationRouteCandidate> _candidates;

        public FakeOrganizationIntegrationResolver(IReadOnlyList<IntegrationRouteCandidate> candidates)
        {
            _candidates = candidates;
        }

        public Task<Result<IntegrationRouteResolution>> ResolveRouteAsync(
            IntegrationRouteRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            var ordered = _candidates.OrderBy(x => x.Priority).ToList();
            return Task.FromResult(Result.Success(new IntegrationRouteResolution(ordered, [])));
        }
    }

    private sealed class FakeCredentialResolver : IIntegrationCredentialResolver
    {
        private readonly string _expectedProviderKey;

        public FakeCredentialResolver(string expectedProviderKey)
        {
            _expectedProviderKey = expectedProviderKey;
        }

        public Task<Result<ResolvedProviderCredential>> ResolveAsync(
            Guid organizationId,
            IntegrationRouteCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Assert.Equal(_expectedProviderKey, candidate.ProviderKey);
            return Task.FromResult(Result.Success(new ResolvedProviderCredential(
                candidate.ProviderId,
                candidate.ProviderKey,
                candidate.ConnectionMode,
                new Dictionary<string, string> { ["api_key"] = "sk-test" },
                candidate.SelectedModel)));
        }
    }

    private sealed class FakeAiCompletionGateway : IAiCompletionGateway
    {
        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            _ = cancellationToken;
            Assert.Equal("ai-analysis", request.CapabilityKey);
            object payload = new TestAnalysisPayload("output");
            return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
                (TResponse)payload,
                request.PreferredProviderKey!,
                request.PreferredModel,
                [])));
        }
    }

    private sealed class FakeSocialContentExtractor : ISocialContentExtractor
    {
        public Task<Result<SocialContentSnapshot>> ExtractAsync(
            SocialContentRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(Result.Success(new SocialContentSnapshot(
                request.PlatformKey,
                request.NormalizedUrl,
                Title: null,
                Description: null,
                Author: null,
                ThumbnailUrl: null,
                DurationSeconds: null,
                Metrics: new Dictionary<string, long>(),
                Transcript: null,
                MediaReferences: [],
                RawPayloadReference: "provider://payload/123",
                Warnings: [new SocialContentWarning("transcript_missing", "social.content.warnings.transcript_missing")],
                Tags: [],
                ContentType: null,
                PublishedAtUtc: null)));
        }
    }

    private sealed record TestAnalysisPayload(string Summary);
}
