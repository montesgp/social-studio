using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.Integrations;

public static class ApifyActorMappingBackfillExtensions
{
    /// <summary>
    /// Runs the idempotent Apify actor_mappings_json backfill at startup.
    /// Re-tags legacy "default" variantKey entries to "profile" / "social-extraction"
    /// with isPrimary flags. Safe to call multiple times — orgs without "default"
    /// entries are skipped without any writes.
    /// </summary>
    public static async Task RunApifyActorMappingBackfillAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var backfill = scope.ServiceProvider.GetRequiredService<ApifyActorMappingBackfill>();
        await backfill.RunAsync(cancellationToken);
    }
}
