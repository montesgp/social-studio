using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Tests.Infrastructure.DependencyInjection;

/// <summary>
/// WU-11b (design §16 Composition row — "the count assertion is the load-bearing one"): exactly one
/// implementation resolves per NicheDiscovery port, and
/// <c>GetServices&lt;INicheOpportunityActivationSource&gt;()</c> is EMPTY — "named, not built" (ND-17,
/// §12) is asserted here, not trusted.
/// </summary>
public sealed class NicheDiscoveryCompositionTests
{
    [Fact]
    public void AddInfrastructure_RegistersExactlyOneImplementation_PerNicheDiscoveryPort()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.Single(scope.ServiceProvider.GetServices<IDiscoveryRunRepository>());
        Assert.Single(scope.ServiceProvider.GetServices<INicheOpportunityRepository>());
        Assert.Single(scope.ServiceProvider.GetServices<INicheDiscoveryEvidenceRepository>());
        Assert.Single(scope.ServiceProvider.GetServices<IMarketCatalogRepository>());
        Assert.Single(scope.ServiceProvider.GetServices<INicheDiscoveryOriginProbe>());
        Assert.Single(scope.ServiceProvider.GetServices<INicheDiscoveryRunQueue>());
        Assert.Single(scope.ServiceProvider.GetServices<INicheDiscoveryRunProcessor>());
    }

    [Fact]
    public void AddInfrastructure_DiscoverySources_HaveDistinctKeysAndOrderValues()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var sources = scope.ServiceProvider.GetServices<INicheDiscoverySource>().ToList();

        Assert.NotEmpty(sources);
        Assert.Equal(sources.Count, sources.Select(s => s.SourceKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(sources.Count, sources.Select(s => s.Order).Distinct().Count());
    }

    [Fact]
    public void AddInfrastructure_INicheOpportunityActivationSource_HasNoRegistration()
    {
        // ND-17/§12: the Content Generator activation seam is named, not built, in this change.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var services = scope.ServiceProvider.GetServices<INicheOpportunityActivationSource>();

        Assert.Empty(services);
    }

    [Fact]
    public void AddInfrastructure_ResolvesTheRunQueue_AsTheChannelBackedImplementation()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var queue = scope.ServiceProvider.GetRequiredService<INicheDiscoveryRunQueue>();

        Assert.IsType<Incoders.Hive.Infrastructure.Social.NicheDiscovery.NicheDiscoveryRunQueue>(queue);
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:ConnectionStringName"] = "DefaultConnection",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=placeholder;Username=placeholder;Password=placeholder",
                ["MultiTenancy:Mode"] = "Header",
                ["SupabaseStorage:Url"] = "https://placeholder.supabase.co",
                ["SupabaseStorage:Bucket"] = "placeholder",
                ["SupabaseStorage:ServiceRoleKey"] = "placeholder",
                ["Supabase:Url"] = "https://placeholder.supabase.co",
                ["Supabase:Bucket"] = "placeholder",
                ["Supabase:ServiceRoleKey"] = "placeholder",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
