using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Integrations;
using SocialStudio.Infrastructure.Social.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Tests.Infrastructure.Integrations;

/// <summary>
/// The actor-catalog capability is routed by provider key exactly like the model catalog:
/// <c>apify</c> resolves, everything else does not — so the sync handler's capability check
/// stays the single source of truth for which connectors show a "Sync actors" affordance.
/// </summary>
public sealed class ApifyActorCatalogFactoryTests
{
    [Fact]
    public void Supports_ActorCatalogClient_IsTrueOnlyForApify()
    {
        var factory = BuildFactory();

        Assert.True(factory.Supports<IActorCatalogClient>("apify"));
        Assert.True(factory.Supports<IActorCatalogClient>("APIFY"));
        Assert.False(factory.Supports<IActorCatalogClient>("data365"));
        Assert.False(factory.Supports<IActorCatalogClient>("claude"));
        Assert.False(factory.Supports<IActorCatalogClient>(""));
    }

    [Fact]
    public void Create_ActorCatalogClient_ForApify_ReturnsApifyActorCatalogClient()
    {
        var factory = BuildFactory();

        var client = factory.Create<IActorCatalogClient>(BuildApifyCredential());

        Assert.IsType<ApifyActorCatalogClient>(client);
    }

    [Fact]
    public void Create_ActorCatalogClient_ForUnsupportedProvider_Throws()
    {
        var factory = BuildFactory();

        Assert.Throws<InvalidOperationException>(() => factory.Create<IActorCatalogClient>(
            new ResolvedProviderCredential(
                Guid.Empty,
                "data365",
                IntegrationConnectionMode.BringYourOwnKey,
                new Dictionary<string, string> { ["api_key"] = "test-key" },
                SelectedModel: null)));
    }

    private static ExternalProviderClientFactory BuildFactory()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        return new ExternalProviderClientFactory(services.BuildServiceProvider());
    }

    private static ResolvedProviderCredential BuildApifyCredential() =>
        new(Guid.Empty, "apify", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-key" }, SelectedModel: null);
}
