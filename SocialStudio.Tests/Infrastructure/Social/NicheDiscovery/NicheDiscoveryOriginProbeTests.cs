using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Social.NicheDiscovery;
using Microsoft.Extensions.Options;

namespace SocialStudio.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// WU-5 (INT-ND-03): reports satisfied once the API key is present AND at least one stage route
/// resolves — exactly the resolution the runtime origin performs.
/// </summary>
public sealed class NicheDiscoveryOriginProbeTests
{
    [Fact]
    public async Task IsReadyAsync_EnabledRoute_WithApiKey_IsReady()
    {
        var probe = new NicheDiscoveryOriginProbe(
            new FakeWorkflowConfigurationResolver(enabled: true),
            new FakeIntegrationResolver("real-key"),
            MakeOptions("https://www.googleapis.com/youtube/v3"));

        Assert.True(await probe.IsReadyAsync(Guid.NewGuid(), "youtube", CancellationToken.None));
    }

    [Fact]
    public async Task IsReadyAsync_NoEnabledRoute_IsNotReady()
    {
        var probe = new NicheDiscoveryOriginProbe(
            new FakeWorkflowConfigurationResolver(enabled: false),
            new FakeIntegrationResolver("real-key"),
            MakeOptions("https://www.googleapis.com/youtube/v3"));

        Assert.False(await probe.IsReadyAsync(Guid.NewGuid(), "youtube", CancellationToken.None));
    }

    [Fact]
    public async Task IsReadyAsync_NoApiKey_IsNotReady()
    {
        var probe = new NicheDiscoveryOriginProbe(
            new FakeWorkflowConfigurationResolver(enabled: true),
            new FakeIntegrationResolver(apiKey: null),
            MakeOptions("https://www.googleapis.com/youtube/v3"));

        Assert.False(await probe.IsReadyAsync(Guid.NewGuid(), "youtube", CancellationToken.None));
    }

    [Fact]
    public async Task IsReadyAsync_NetworkOtherThanYouTube_IsNeverReady()
    {
        var probe = new NicheDiscoveryOriginProbe(
            new FakeWorkflowConfigurationResolver(enabled: true),
            new FakeIntegrationResolver("real-key"),
            MakeOptions("https://www.googleapis.com/youtube/v3"));

        Assert.False(await probe.IsReadyAsync(Guid.NewGuid(), "tiktok", CancellationToken.None));
    }

    [Fact]
    public async Task IsReadyAsync_BlankBaseAddress_IsNotReady()
    {
        var probe = new NicheDiscoveryOriginProbe(
            new FakeWorkflowConfigurationResolver(enabled: true),
            new FakeIntegrationResolver("real-key"),
            MakeOptions(baseAddress: null));

        Assert.False(await probe.IsReadyAsync(Guid.NewGuid(), "youtube", CancellationToken.None));
    }

    private static IOptions<NicheDiscoveryOriginOptions> MakeOptions(string? baseAddress) =>
        Options.Create(new NicheDiscoveryOriginOptions { BaseAddress = baseAddress });

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
                IntegrationProviderCategory.DataExtraction,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: credentials)));
        }
    }
}
