using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Social.NicheDiscovery;
using Microsoft.Extensions.Options;

namespace SocialStudio.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// WU-5 (INT-ND-04): mirrors <c>ApifyNicheSearchActorConfiguration</c>/<c>ApifyNicheSearchResolverTests</c>
/// exactly. Resolution order: an enabled workflow-step route with a matching provider key wins first;
/// otherwise <c>Unconfigured</c> — never a literal, never a throw. A blank
/// <see cref="NicheDiscoveryOriginOptions.BaseAddress"/> degrades to <c>Unconfigured</c> before any
/// resolution call is even made.
/// </summary>
public sealed class NicheDiscoveryOriginConfigurationTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public async Task ResolveAsync_EnabledWorkflowStepRoute_WithApiKey_IsConfigured()
    {
        var configuration = new NicheDiscoveryOriginConfiguration(
            new FakeWorkflowConfigurationResolver(
                Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(), "youtube-data-api-v3", Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                        ModelId: null, ActorId: null, IsEnabled: true),
                ])),
            new FakeIntegrationResolver("real-api-key"),
            MakeOptions("https://www.googleapis.com/youtube/v3"));

        var route = await configuration.ResolveAsync(OrganizationId, DiscoveryStage.ChannelSearch, "youtube", CancellationToken.None);

        Assert.True(route.IsConfigured);
        Assert.Equal("real-api-key", route.ApiKey);
        Assert.Equal("https://www.googleapis.com/youtube/v3", route.BaseAddress);
    }

    [Fact]
    public async Task ResolveAsync_NoEnabledRoute_IsUnconfigured_NeverThrows()
    {
        var configuration = new NicheDiscoveryOriginConfiguration(
            new FakeWorkflowConfigurationResolver(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([])),
            new FakeIntegrationResolver("real-api-key"),
            MakeOptions("https://www.googleapis.com/youtube/v3"));

        var route = await configuration.ResolveAsync(OrganizationId, DiscoveryStage.ChannelSearch, "youtube", CancellationToken.None);

        Assert.False(route.IsConfigured);
        Assert.Null(route.ApiKey);
    }

    [Fact]
    public async Task ResolveAsync_EnabledRoute_ButNoApiKeyCredential_IsUnconfigured()
    {
        var configuration = new NicheDiscoveryOriginConfiguration(
            new FakeWorkflowConfigurationResolver(
                Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(), "youtube-data-api-v3", Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                        ModelId: null, ActorId: null, IsEnabled: true),
                ])),
            new FakeIntegrationResolver(apiKey: null),
            MakeOptions("https://www.googleapis.com/youtube/v3"));

        var route = await configuration.ResolveAsync(OrganizationId, DiscoveryStage.ChannelSearch, "youtube", CancellationToken.None);

        Assert.False(route.IsConfigured);
    }

    [Fact]
    public async Task ResolveAsync_DisabledRoute_IsUnconfigured()
    {
        var configuration = new NicheDiscoveryOriginConfiguration(
            new FakeWorkflowConfigurationResolver(
                Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(), "youtube-data-api-v3", Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                        ModelId: null, ActorId: null, IsEnabled: false),
                ])),
            new FakeIntegrationResolver("real-api-key"),
            MakeOptions("https://www.googleapis.com/youtube/v3"));

        var route = await configuration.ResolveAsync(OrganizationId, DiscoveryStage.ChannelSearch, "youtube", CancellationToken.None);

        Assert.False(route.IsConfigured);
    }

    [Fact]
    public async Task ResolveAsync_BlankBaseAddress_IsUnconfigured_BeforeAnyResolutionCall()
    {
        var workflowResolver = new CountingWorkflowConfigurationResolver();

        var configuration = new NicheDiscoveryOriginConfiguration(
            workflowResolver,
            new FakeIntegrationResolver("real-api-key"),
            MakeOptions(baseAddress: null));

        var route = await configuration.ResolveAsync(OrganizationId, DiscoveryStage.ChannelSearch, "youtube", CancellationToken.None);

        Assert.False(route.IsConfigured);
        Assert.Equal(0, workflowResolver.CallCount);
    }

    private static IOptions<NicheDiscoveryOriginOptions> MakeOptions(string? baseAddress) =>
        Options.Create(new NicheDiscoveryOriginOptions { BaseAddress = baseAddress });

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

    private sealed class CountingWorkflowConfigurationResolver : IWorkflowConfigurationResolver
    {
        public int CallCount { get; private set; }

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]));
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
                ProviderDisplayName: "YouTube Data API v3 (test)",
                Category: IntegrationProviderCategory.DataExtraction,
                ConnectionMode: IntegrationConnectionMode.BringYourOwnKey,
                Status: IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: credentials)));
        }
    }
}
