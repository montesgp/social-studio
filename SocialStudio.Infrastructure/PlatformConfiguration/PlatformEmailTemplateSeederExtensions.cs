using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.PlatformConfiguration;

public static class PlatformEmailTemplateSeederExtensions
{
    public static async Task SeedPlatformEmailTemplatesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<PlatformEmailTemplateSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
