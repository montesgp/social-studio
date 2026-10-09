using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Incoders.Hive.Infrastructure.Integrations.HealthChecks;

namespace Incoders.Hive.Tests.Infrastructure.Integrations;

public sealed class ContentAnalysisPreflightTests
{
    [Fact]
    public async Task EnsureReady_Succeeds_WhenBothProvidersHealthy()
    {
        var preflight = new ContentAnalysisPreflight(
            new FakeResolver("apify", "claude"),
            new IExternalIntegrationHealthCheck[] { Hc("apify", true), Hc("claude", true) });

        var result = await preflight.EnsureReadyAsync(Guid.NewGuid(), "apify", "claude", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureReady_FailsExtraction_WhenExtractionProviderNotConnected()
    {
        var preflight = new ContentAnalysisPreflight(
            new FakeResolver("claude"), // apify not connected
            new IExternalIntegrationHealthCheck[] { Hc("apify", true), Hc("claude", true) });

        var result = await preflight.EnsureReadyAsync(Guid.NewGuid(), "apify", "claude", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SingleAnalyzerErrors.ExtractionProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task EnsureReady_FailsAi_WhenAiHealthCheckUnhealthy()
    {
        var preflight = new ContentAnalysisPreflight(
            new FakeResolver("apify", "claude"),
            new IExternalIntegrationHealthCheck[] { Hc("apify", true), Hc("claude", false) });

        var result = await preflight.EnsureReadyAsync(Guid.NewGuid(), "apify", "claude", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SingleAnalyzerErrors.AiProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task EnsureReady_FailsExtraction_WhenBothProvidersUnhealthy()
    {
        // Extraction keeps reporting priority when both probes fail — matches the previous
        // sequential short-circuit behavior even though both now run concurrently.
        var preflight = new ContentAnalysisPreflight(
            new FakeResolver("apify", "claude"),
            new IExternalIntegrationHealthCheck[] { Hc("apify", false), Hc("claude", false) });

        var result = await preflight.EnsureReadyAsync(Guid.NewGuid(), "apify", "claude", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SingleAnalyzerErrors.ExtractionProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task EnsureReady_NeverProbesBothProvidersConcurrently()
    {
        // Regression test for the production DbContext concurrency crash: extraction and AI
        // probes both go through IIntegrationResolver, which in production is backed by a single
        // scoped DbContext shared across the whole request. Running the two probes truly
        // concurrently (Task.WhenAll) throws "A second operation was started on this context
        // instance..." — see logs/hive-api.log 2026-09-09T20:50:17 / 21:50:27. Probing MUST be
        // sequential so at most one resolver call is ever in flight at a time.
        var resolver = new ConcurrencyTrackingResolver("apify", "claude");
        var preflight = new ContentAnalysisPreflight(
            resolver,
            new IExternalIntegrationHealthCheck[] { Hc("apify", true), Hc("claude", true) });

        var result = await preflight.EnsureReadyAsync(Guid.NewGuid(), "apify", "claude", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, resolver.MaxObservedConcurrency);
    }

    private sealed class ConcurrencyTrackingResolver : IIntegrationResolver
    {
        private readonly HashSet<string> _connected;
        private int _inFlight;

        public ConcurrencyTrackingResolver(params string[] connectedProviders)
        {
            _connected = new HashSet<string>(connectedProviders, StringComparer.OrdinalIgnoreCase);
        }

        public int MaxObservedConcurrency { get; private set; }

        public async Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _inFlight);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
            try
            {
                // Simulate real query latency so a concurrent second call would overlap this one
                // if the caller failed to serialize the two probes.
                await Task.Delay(50, cancellationToken);

                if (!_connected.Contains(providerKey))
                {
                    return Result.Failure<ResolvedIntegration>(IntegrationErrors.IntegrationNotConnected);
                }

                return Result.Success(new ResolvedIntegration(
                    organizationId,
                    providerKey,
                    providerKey,
                    IntegrationProviderCategory.DataExtraction,
                    IntegrationConnectionMode.BringYourOwnKey,
                    IntegrationStatus.Connected,
                    IsReadOnly: false,
                    SelectedAiModel: null,
                    new Dictionary<string, string> { ["api_key"] = "test" }));
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private static IExternalIntegrationHealthCheck Hc(string providerKey, bool healthy) =>
        new FakeHealthCheck(providerKey, healthy);

    private sealed class FakeHealthCheck : IExternalIntegrationHealthCheck
    {
        private readonly bool _healthy;

        public FakeHealthCheck(string providerKey, bool healthy)
        {
            ProviderKey = providerKey;
            _healthy = healthy;
        }

        public string ProviderKey { get; }

        public Task<IntegrationHealthResult> CheckAsync(
            IReadOnlyDictionary<string, string> credentials,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntegrationHealthResult(
                _healthy,
                ProviderKey,
                _healthy ? 200 : 401,
                _healthy ? null : "invalid_credentials",
                _healthy ? "OK" : "rejected"));
    }

    private sealed class FakeResolver : IIntegrationResolver
    {
        private readonly HashSet<string> _connected;

        public FakeResolver(params string[] connectedProviders)
        {
            _connected = new HashSet<string>(connectedProviders, StringComparer.OrdinalIgnoreCase);
        }

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default)
        {
            if (!_connected.Contains(providerKey))
            {
                return Task.FromResult(Result.Failure<ResolvedIntegration>(IntegrationErrors.IntegrationNotConnected));
            }

            return Task.FromResult(Result.Success(new ResolvedIntegration(
                organizationId,
                providerKey,
                providerKey,
                IntegrationProviderCategory.DataExtraction,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                new Dictionary<string, string> { ["api_key"] = "test" })));
        }
    }
}
