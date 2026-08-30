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
    public async Task EnsureReady_ProbesBothProvidersConcurrently()
    {
        var probeDelay = TimeSpan.FromMilliseconds(300);
        var preflight = new ContentAnalysisPreflight(
            new FakeResolver("apify", "claude"),
            new IExternalIntegrationHealthCheck[]
            {
                new DelayedHealthCheck("apify", true, probeDelay),
                new DelayedHealthCheck("claude", true, probeDelay),
            });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await preflight.EnsureReadyAsync(Guid.NewGuid(), "apify", "claude", CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.IsSuccess);
        // Sequential probing would take ~2x probeDelay; concurrent probing takes ~1x probeDelay.
        Assert.True(
            stopwatch.Elapsed < probeDelay * 2,
            $"Expected concurrent probing (~{probeDelay}), but took {stopwatch.Elapsed}.");
    }

    private static IExternalIntegrationHealthCheck Hc(string providerKey, bool healthy) =>
        new FakeHealthCheck(providerKey, healthy);

    private sealed class DelayedHealthCheck : IExternalIntegrationHealthCheck
    {
        private readonly bool _healthy;
        private readonly TimeSpan _delay;

        public DelayedHealthCheck(string providerKey, bool healthy, TimeSpan delay)
        {
            ProviderKey = providerKey;
            _healthy = healthy;
            _delay = delay;
        }

        public string ProviderKey { get; }

        public async Task<IntegrationHealthResult> CheckAsync(
            IReadOnlyDictionary<string, string> credentials,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            return new IntegrationHealthResult(
                _healthy,
                ProviderKey,
                _healthy ? 200 : 401,
                _healthy ? null : "invalid_credentials",
                _healthy ? "OK" : "rejected");
        }
    }

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
