using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.Social.NicheDiscovery;

public static class MarketCatalogSeederExtensions
{
    public static async Task SeedMarketCatalogAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<MarketCatalogSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
