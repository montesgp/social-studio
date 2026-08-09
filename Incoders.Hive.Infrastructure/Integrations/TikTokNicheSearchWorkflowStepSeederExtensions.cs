using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Infrastructure.Integrations;

public static class TikTokNicheSearchWorkflowStepSeederExtensions
{
    public static async Task SeedTikTokNicheSearchWorkflowStepsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<TikTokNicheSearchWorkflowStepSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
