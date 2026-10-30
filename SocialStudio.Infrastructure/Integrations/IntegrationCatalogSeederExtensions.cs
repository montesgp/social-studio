using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.Integrations;

public static class IntegrationCatalogSeederExtensions
{
    public static async Task SeedIntegrationCatalogAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IntegrationCatalogSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}