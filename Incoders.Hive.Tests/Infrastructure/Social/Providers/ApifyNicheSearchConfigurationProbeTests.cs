using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Social.Providers;

/// <summary>
/// The readiness probe behind the integrations checklist MUST agree with what
/// <see cref="ApifyNicheSearchResolver"/> actually dispatches: an actor mapped in the Apify connector
/// counts as configured even when the seeded workflow-step row is still empty. Before this, the UI said
/// "not configured" while the backend was already calling that actor.
/// </summary>
public sealed class ApifyNicheSearchConfigurationProbeTests
{
    private static readonly string NicheSearchMappingsJson = JsonSerializer.Serialize(new[]
    {
        new { networkKey = "tiktok", variantKey = "niche-search", actorId = "connector-mapped-actor", isPrimary = true },
    });

    [Fact]
    public void SupportedNetworks_ListsOnlyNetworksWithAWorkingAdapter()
    {
        var probe = new ApifyNicheSearchConfigurationProbe(
            new FakeWorkflowConfigurationResolver(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([])),
            new FakeIntegrationResolver(null));

        Assert.Equal(["tiktok"], probe.SupportedNetworks.Select(x => x.NetworkKey));
        Assert.Equal(["TikTok"], probe.SupportedNetworks.Select(x => x.DisplayName));
    }

    [Fact]
    public async Task ResolvesActorAsync_EnabledWorkflowStepRoute_IsConfigured()
    {
        var probe = new ApifyNicheSearchConfigurationProbe(
            new FakeWorkflowConfigurationResolver(
                Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(), "apify", Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                        ModelId: null, ActorId: "workflow-routed-actor", IsEnabled: true),
                ])),
            new FakeIntegrationResolver(null));

        Assert.True(await probe.ResolvesActorAsync(Guid.NewGuid(), "tiktok", CancellationToken.None));
    }

    [Fact]
    public async Task ResolvesActorAsync_ConnectorMappingOnly_IsAlsoConfigured()
    {
        // The exact drift this probe was extracted to remove: the workflow row is empty, but the raw
        // actor_mappings_json entry resolves — and the runtime resolver dispatches to it.
        var probe = new ApifyNicheSearchConfigurationProbe(
            new FakeWorkflowConfigurationResolver(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([])),
            new FakeIntegrationResolver(NicheSearchMappingsJson));

        Assert.True(await probe.ResolvesActorAsync(Guid.NewGuid(), "tiktok", CancellationToken.None));
    }

    [Fact]
    public async Task ResolvesActorAsync_NoRouteAndNoMapping_IsNotConfigured()
    {
        var probe = new ApifyNicheSearchConfigurationProbe(
            new FakeWorkflowConfigurationResolver(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([])),
            new FakeIntegrationResolver("[]"));

        Assert.False(await probe.ResolvesActorAsync(Guid.NewGuid(), "tiktok", CancellationToken.None));
    }

    [Fact]
    public async Task ResolvesActorAsync_NetworkWithoutAnAdapter_IsNeverConfigured_EvenWithAMapping()
    {
        var instagramMappings = JsonSerializer.Serialize(new[]
        {
            new { networkKey = "instagram", variantKey = "niche-search", actorId = "instagram-actor", isPrimary = true },
        });

        var probe = new ApifyNicheSearchConfigurationProbe(
            new FakeWorkflowConfigurationResolver(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([])),
            new FakeIntegrationResolver(instagramMappings));

        // No adapter exists, so it could never dispatch — reporting "configured" would be a lie.
        Assert.False(await probe.ResolvesActorAsync(Guid.NewGuid(), "instagram", CancellationToken.None));
    }

    [Fact]
    public async Task ResolvesActorAsync_DisabledWorkflowRouteWithNoMapping_IsNotConfigured()
    {
        var probe = new ApifyNicheSearchConfigurationProbe(
            new FakeWorkflowConfigurationResolver(
                Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(), "apify", Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey, Priority: 1,
                        ModelId: null, ActorId: "disabled-actor", IsEnabled: false),
                ])),
            new FakeIntegrationResolver("[]"));

        Assert.False(await probe.ResolvesActorAsync(Guid.NewGuid(), "tiktok", CancellationToken.None));
    }

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

    private sealed class FakeIntegrationResolver : IIntegrationResolver
    {
        private readonly string? _actorMappingsJson;

        public FakeIntegrationResolver(string? actorMappingsJson)
        {
            _actorMappingsJson = actorMappingsJson;
        }

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default)
        {
            var credentials = new Dictionary<string, string> { ["api_key"] = "test-key" };
            if (_actorMappingsJson is not null)
            {
                credentials["actor_mappings_json"] = _actorMappingsJson;
            }

            return Task.FromResult(Result.Success(new ResolvedIntegration(
                organizationId,
                providerKey,
                ProviderDisplayName: "Apify (test)",
                Category: IntegrationProviderCategory.DataExtraction,
                ConnectionMode: IntegrationConnectionMode.BringYourOwnKey,
                Status: IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: credentials)));
        }
    }
}
