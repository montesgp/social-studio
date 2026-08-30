using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Infrastructure.Social.Discovery;

public static class DiscoverySeedImporterExtensions
{
    public static async Task ImportDiscoverySeedAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<DiscoverySeedImporter>();
        await importer.ImportAsync(cancellationToken);
    }
}
