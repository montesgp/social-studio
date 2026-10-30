using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.PlatformConfiguration;

public static class PlatformSystemModuleSeederExtensions
{
    public static async Task SeedPlatformSystemModulesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<PlatformSystemModuleSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
