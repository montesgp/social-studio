using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Social;

namespace Incoders.Hive.Tests.Infrastructure.Social;

public sealed class SocialContentResolverTests
{
    [Fact]
    public async Task ResolveAsync_PreservesPartialExtractionWarnings()
    {
        var candidate = Candidate("apify");
        var warning = new SocialContentWarning("transcript_missing", "social.warnings.transcript_missing");
        var extractor = new StaticExtractor(new SocialContentSnapshot(
            "tiktok",
            "https://www.tiktok.com/@brand/video/123",
            Title: "Hook example",
            Description: null,
            Author: "@brand",
            ThumbnailUrl: null,
            DurationSeconds: null,
            Metrics: new Dictionary<string, long>(),
            Transcript: null,
            MediaReferences: [],
            RawPayloadReference: "provider://apify/run/1",
            Warnings: [warning]));

        var resolver = new SocialContentResolver(
            new StaticRouteResolver(candidate),
            new StaticCredentialResolver(),
            new StaticClientFactory(extractor),
            new IntegrationExecutionPolicy(new ProviderFallbackPolicy(), new InMemoryAttemptRepository(), TimeProvider.System));

        var result = await resolver.ResolveAsync(
            new SocialContentResolutionRequest(
                Guid.NewGuid(),
                ClientId: null,
                SourceUrl: "https://www.tiktok.com/@brand/video/123",
                PlatformHint: "tiktok",
                PreferredProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("tiktok", result.Value.Snapshot.PlatformKey);
        Assert.Single(result.Value.Snapshot.Warnings);
        Assert.Equal("transcript_missing", result.Value.Snapshot.Warnings[0].Code);
    }

    private static IntegrationRouteCandidate Candidate(string providerKey) =>
        IntegrationRouteCandidate.Create(
            Guid.NewGuid(),
            providerKey,
            IntegrationConnectionMode.BringYourOwnKey,
            priority: 1,
            allowManagedFallback: true,
            maxAttempts: 1,
            timeoutMs: 2_000,
            selectedModel: null,
            isEnabled: true).Value;

    private sealed class StaticExtractor : ISocialContentExtractor
    {
        private readonly SocialContentSnapshot _snapshot;

        public StaticExtractor(SocialContentSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<Result<SocialContentSnapshot>> ExtractAsync(
            SocialContentRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(_snapshot));
    }

    private sealed class StaticRouteResolver : IOrganizationIntegrationResolver
    {
        private readonly IntegrationRouteCandidate _candidate;

        public StaticRouteResolver(IntegrationRouteCandidate candidate)
        {
            _candidate = candidate;
        }

        public Task<Result<IntegrationRouteResolution>> ResolveRouteAsync(
            IntegrationRouteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new IntegrationRouteResolution([_candidate], [])));
    }

    private sealed class StaticCredentialResolver : IIntegrationCredentialResolver
    {
        public Task<Result<ResolvedProviderCredential>> ResolveAsync(
            Guid organizationId,
            IntegrationRouteCandidate candidate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new ResolvedProviderCredential(
                candidate.ProviderId,
                candidate.ProviderKey,
                candidate.ConnectionMode,
                new Dictionary<string, string> { ["api_key"] = "test" },
                candidate.SelectedModel)));
    }

    private sealed class StaticClientFactory : IExternalProviderClientFactory
    {
        private readonly ISocialContentExtractor _extractor;

        public StaticClientFactory(ISocialContentExtractor extractor)
        {
            _extractor = extractor;
        }

        public TClient Create<TClient>(ResolvedProviderCredential credential)
            where TClient : notnull =>
            (TClient)_extractor;
    }

    private sealed class InMemoryAttemptRepository : IIntegrationExecutionAttemptRepository
    {
        public Task AddAsync(IntegrationExecutionAttemptRecord attempt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
            Guid analysisId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IntegrationExecutionAttemptRecord>>([]);
    }
}
