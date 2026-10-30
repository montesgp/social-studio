using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;
using SocialStudio.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Tests.Infrastructure.DependencyInjection;

/// <summary>
/// WU-2 (§4 "the DI replacement, the trap, handled"): the single most important regression guard in
/// this change. <c>services.AddScoped&lt;IContentDiscoveryService, ReferenceAccountContentDiscovery&gt;()</c>
/// must be REPLACED, never supplemented — a second registration would silently win
/// <c>GetRequiredService</c> with no compile or runtime error.
/// </summary>
public sealed class DiscoveryCompositionTests
{
    [Fact]
    public void AddInfrastructure_ResolvesIContentDiscoveryService_AsContentDiscoveryChain()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IContentDiscoveryService>();

        Assert.IsType<ContentDiscoveryChain>(service);
    }

    [Fact]
    public void AddInfrastructure_RegistersExactlyOneIContentDiscoveryService()
    {
        // THIS is the assertion that catches a silent DI override (design §4) — written first.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var services = scope.ServiceProvider.GetServices<IContentDiscoveryService>();

        Assert.Single(services);
    }

    [Fact]
    public void AddInfrastructure_RegistersAllThreeDiscoverySources_WithExpectedKeys()
    {
        // references (WU-3), own (WU-3), niche (WU-6, TikTok niche-search) — all three land here.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IContentDiscoverySource>().ToList();

        Assert.Equal(
            new[] { "references", "own", "niche" }.OrderBy(x => x),
            sources.Select(s => s.SourceKey).OrderBy(x => x));
    }

    [Fact]
    public void AddInfrastructure_DiscoverySources_HaveDistinctOrderValues()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IContentDiscoverySource>().ToList();

        Assert.Equal(sources.Count, sources.Select(s => s.Order).Distinct().Count());
    }

    // ── harness ──────────────────────────────────────────────────────────────

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
