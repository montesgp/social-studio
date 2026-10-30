using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Infrastructure.DependencyInjection;
using SocialStudio.Infrastructure.Social.ContentGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Tests.Infrastructure.DependencyInjection;

/// <summary>
/// TDD tests for the <c>Storage:AssetProvider</c>-driven registration of
/// <see cref="IGeneratedAssetStorage"/>. "Local" (case-insensitive) resolves to
/// <see cref="LocalFileGeneratedAssetStorage"/> WITHOUT requiring any Supabase secrets.
/// Anything else (including the default/unset value) keeps the existing Supabase
/// HttpClient adapter registration exactly as-is.
/// </summary>
public sealed class LocalAssetStorageDiResolutionTests
{
    [Fact]
    public void AddInfrastructure_StorageAssetProviderLocal_ResolvesLocalFileGeneratedAssetStorage()
    {
        var services = BuildServices(assetProvider: "Local", includeSupabaseSecrets: false);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IGeneratedAssetStorage>();

        Assert.IsType<LocalFileGeneratedAssetStorage>(storage);
    }

    [Fact]
    public void AddInfrastructure_StorageAssetProviderLocal_CaseInsensitive_Resolves()
    {
        var services = BuildServices(assetProvider: "local", includeSupabaseSecrets: false);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IGeneratedAssetStorage>();

        Assert.IsType<LocalFileGeneratedAssetStorage>(storage);
    }

    [Fact]
    public void AddInfrastructure_StorageAssetProviderLocal_DoesNotRegisterSupabaseOptions()
    {
        var services = BuildServices(assetProvider: "Local", includeSupabaseSecrets: false);

        var hasSupabaseOptions = services.Any(d => d.ServiceType == typeof(SupabaseStorageOptions));
        Assert.False(hasSupabaseOptions);
    }

    [Fact]
    public void AddInfrastructure_StorageAssetProviderDefault_KeepsSupabaseAdapter()
    {
        var services = BuildServices(assetProvider: null, includeSupabaseSecrets: true);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IGeneratedAssetStorage>();

        Assert.IsType<SupabaseStorageAdapter>(storage);
    }

    private static IServiceCollection BuildServices(string? assetProvider, bool includeSupabaseSecrets)
    {
        var config = new Dictionary<string, string?>
        {
            ["Persistence:ConnectionStringName"] = "DefaultConnection",
            ["MultiTenancy:Mode"] = "Header",
        };

        if (assetProvider is not null)
        {
            config["Storage:AssetProvider"] = assetProvider;
        }

        if (includeSupabaseSecrets)
        {
            config["Supabase:Url"] = "https://placeholder.supabase.co";
            config["Supabase:Bucket"] = "placeholder";
            config["Supabase:ServiceRoleKey"] = "placeholder";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services;
    }
}
