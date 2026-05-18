using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Infrastructure.Social;

public static class SocialNetworkCatalogSeederExtensions
{
    public static async Task SeedSocialNetworkCatalogAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<SocialNetworkCatalogSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
