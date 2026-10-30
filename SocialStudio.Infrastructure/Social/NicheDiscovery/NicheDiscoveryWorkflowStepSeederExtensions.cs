using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.Social.NicheDiscovery;

public static class NicheDiscoveryWorkflowStepSeederExtensions
{
    public static async Task SeedNicheDiscoveryWorkflowStepsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<NicheDiscoveryWorkflowStepSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
