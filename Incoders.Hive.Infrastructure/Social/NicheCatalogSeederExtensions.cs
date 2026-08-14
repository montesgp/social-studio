using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Infrastructure.Social;

public static class NicheCatalogSeederExtensions
{
    public static async Task SeedNicheCatalogAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<NicheCatalogSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
