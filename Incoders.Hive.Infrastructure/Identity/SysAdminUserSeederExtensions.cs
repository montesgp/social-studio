using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Infrastructure.Identity;

public static class SysAdminUserSeederExtensions
{
    public static async Task SeedSysAdminUserAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<SysAdminUserSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
