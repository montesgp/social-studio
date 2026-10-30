using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Infrastructure.Identity;

/// <summary>
/// Ensures every role declared by <see cref="IdentityRoles"/> exists in <c>identity.roles</c>.
/// Idempotent: existing roles are left untouched.
/// </summary>
public sealed class RoleSeeder
{
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly ILogger<RoleSeeder> _logger;

    public RoleSeeder(RoleManager<ApplicationRole> roles, ILogger<RoleSeeder> logger)
    {
        _roles = roles;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in IdentityRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _roles.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await _roles.CreateAsync(new ApplicationRole(roleName));
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
                throw new InvalidOperationException($"Failed to seed role '{roleName}': {errors}");
            }

            _logger.LogInformation("Seeded identity role: {Role}", roleName);
        }
    }
}

public static class RoleSeederExtensions
{
    public static async Task SeedIdentityRolesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<RoleSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
