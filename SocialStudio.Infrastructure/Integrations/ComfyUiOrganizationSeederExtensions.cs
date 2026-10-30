using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.Integrations;

public static class ComfyUiOrganizationSeederExtensions
{
    public static async Task SeedComfyUiOrganizationIntegrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<ComfyUiOrganizationSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
