using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Social;

namespace Incoders.Hive.Tests.Infrastructure.Social;

public sealed class SocialContentResolverTests
{
    [Fact]
    public async Task ResolveAsync_ResolvesExtractionProviderDirectly()
    {
        var warning = new SocialContentWarning("transcript_missing", "social.warnings.transcript_missing");
        var extractor = new StaticExtractor(Snapshot(warnings: [warning]));
        var integrationResolver = new StaticIntegrationResolver("apify");

        var resolver = new SocialContentResolver(integrationResolver, new StaticClientFactory(extractor));

        var result = await resolver.ResolveAsync(
            new SocialContentResolutionRequest(
                Guid.NewGuid(),
                AnalysisId: null,
                ClientId: null,
                SourceUrl: "https://www.tiktok.com/@brand/video/123",
                PlatformHint: "tiktok",
                PreferredProviderKey: "apify"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("apify", integrationResolver.LastProviderKey);
        Assert.Equal("tiktok", result.Value.Snapshot.PlatformKey);
        Assert.Single(result.Value.Snapshot.Warnings);
        Assert.Equal("transcript_missing", result.Value.Snapshot.Warnings[0].Code);
    }

    [Fact]
    public async Task ResolveAsync_DefaultsToApify_WhenNoProviderKeyProvided()
    {
        var integrationResolver = new StaticIntegrationResolver("apify");
        var resolver = new SocialContentResolver(
            integrationResolver,
            new StaticClientFactory(new StaticExtractor(Snapshot())));

        var result = await resolver.ResolveAsync(
            new SocialContentResolutionRequest(
                Guid.NewGuid(),
                AnalysisId: null,
                ClientId: null,
                SourceUrl: "https://www.tiktok.com/@brand/video/123",
                PlatformHint: "tiktok",
                PreferredProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("apify", integrationResolver.LastProviderKey);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsProviderError_WhenExtractionProviderNotConnected()
    {
        var integrationResolver = new StaticIntegrationResolver(
            Result.Failure<ResolvedIntegration>(IntegrationErrors.IntegrationNotConnected));
        var resolver = new SocialContentResolver(
            integrationResolver,
            new StaticClientFactory(new StaticExtractor(Snapshot())));

        var result = await resolver.ResolveAsync(
            new SocialContentResolutionRequest(
                Guid.NewGuid(),
                AnalysisId: null,
                ClientId: null,
                SourceUrl: "https://www.tiktok.com/@brand/video/123",
                PlatformHint: "tiktok",
                PreferredProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.IntegrationNotConnected.Code, result.Error.Code);
    }

    [Fact]
    public async Task ResolveAsync_TriesPreferredActorFallbacksInPriorityOrder()
    {
        var extractor = new ActorAwareExtractor(
            failureActorId: "actor-primary",
            success: Snapshot(platform: "instagram"));
        var integrationResolver = new StaticIntegrationResolver("apify");
        var resolver = new SocialContentResolver(integrationResolver, new StaticClientFactory(extractor));

        var result = await resolver.ResolveAsync(
            new SocialContentResolutionRequest(
                Guid.NewGuid(),
                AnalysisId: null,
                ClientId: null,
                SourceUrl: "https://www.instagram.com/reel/abc",
                PlatformHint: "instagram",
                PreferredProviderKey: "apify",
                PreferredActorIds: ["actor-primary", "actor-fallback"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["actor-primary", "actor-fallback"], extractor.ActorAttempts);
        Assert.Single(result.Value.Diagnostics);
        Assert.Equal("apify", result.Value.Diagnostics[0].ProviderKey);
    }

    private static SocialContentSnapshot Snapshot(
        string platform = "tiktok",
        IReadOnlyList<SocialContentWarning>? warnings = null) =>
        new(
            platform,
            $"https://www.{platform}.com/@brand/video/123",
            Title: "Hook example",
            Description: null,
            Author: "@brand",
            ThumbnailUrl: null,
            DurationSeconds: null,
            Metrics: new Dictionary<string, long>(),
            Transcript: null,
            MediaReferences: [],
            RawPayloadReference: "provider://apify/run/1",
            Warnings: warnings ?? [],
            Tags: [],
            ContentType: null,
            PublishedAtUtc: null);

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

    private sealed class ActorAwareExtractor : ISocialContentExtractor
    {
        private readonly string _failureActorId;
        private readonly SocialContentSnapshot _success;

        public ActorAwareExtractor(string failureActorId, SocialContentSnapshot success)
        {
            _failureActorId = failureActorId;
            _success = success;
        }

        public List<string> ActorAttempts { get; } = [];

        public Task<Result<SocialContentSnapshot>> ExtractAsync(
            SocialContentRequest request,
            CancellationToken cancellationToken = default)
        {
            ActorAttempts.Add(request.ActorId ?? string.Empty);
            return Task.FromResult(
                request.ActorId == _failureActorId
                    ? Result.Failure<SocialContentSnapshot>(IntegrationErrors.ExternalProviderUnavailable)
                    : Result.Success(_success));
        }
    }

    private sealed class StaticIntegrationResolver : IIntegrationResolver
    {
        private readonly Result<ResolvedIntegration> _result;

        public string? LastProviderKey { get; private set; }

        public StaticIntegrationResolver(string providerKey)
            => _result = Result.Success(new ResolvedIntegration(
                Guid.NewGuid(),
                providerKey,
                providerKey,
                IntegrationProviderCategory.DataExtraction,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                IsDefaultAi: false,
                SelectedAiModel: null,
                new Dictionary<string, string>
                {
                    ["api_key"] = "test",
                    ["actor_mappings_json"] = "[]"
                }));

        public StaticIntegrationResolver(Result<ResolvedIntegration> result) => _result = result;

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default)
        {
            LastProviderKey = providerKey;
            return Task.FromResult(_result);
        }

        public Task<Result<ResolvedIntegration>> ResolveDefaultAiProviderAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
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
}
