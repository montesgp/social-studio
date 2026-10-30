using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.Integrations;

public static class ClaudePromptCliOrganizationSeederExtensions
{
    public static async Task SeedClaudePromptCliOrganizationIntegrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<ClaudePromptCliOrganizationSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
