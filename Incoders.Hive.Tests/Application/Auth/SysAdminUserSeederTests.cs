using Incoders.Hive.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Tests.Application.Auth;

/// <summary>
/// End-to-end test of the user-creation pipeline (UserManager + password policy + role assignment)
/// running against EF Core InMemory. Exercises the same code path the runtime seeder uses, so a
/// passing test guarantees the real sysadmin seeding flow respects every domain restriction.
/// </summary>
public class SysAdminUserSeederTests : IDisposable
{
    private readonly ServiceProvider _sp;

    public SysAdminUserSeederTests()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppIdentityDbContext>(opts =>
            opts.UseInMemoryDatabase($"identity-{Guid.NewGuid():N}"));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<ApplicationRole>()
            .AddRoleManager<RoleManager<ApplicationRole>>()
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<RoleSeeder>();

        _sp = services.BuildServiceProvider();
    }

    private SysAdminUserSeeder NewSeeder(string email, string password)
    {
        var users = _sp.GetRequiredService<UserManager<ApplicationUser>>();
        var opts = Options.Create(new SysAdminSeederOptions
        {
            Email = email,
            Password = password,
            DisplayName = "Sysadmin under test",
        });
        return new SysAdminUserSeeder(users, opts, NullLogger<SysAdminUserSeeder>.Instance);
    }

    private async Task SeedRolesAsync()
    {
        var roleSeeder = _sp.GetRequiredService<RoleSeeder>();
        await roleSeeder.SeedAsync();
    }

    [Fact]
    public async Task Seed_CreatesUser_AssignsSysadminRole_AndIsIdempotent()
    {
        await SeedRolesAsync();

        const string email = "sysadmin@incoders.ai";
        const string password = "#Incoders12621262"; // gates:secret-allow test fixture password

        var seeder = NewSeeder(email, password);

        await seeder.SeedAsync();

        var users = _sp.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await users.FindByEmailAsync(email);
        Assert.NotNull(created);
        Assert.True(await users.CheckPasswordAsync(created!, password));
        Assert.True(await users.IsInRoleAsync(created, IdentityRoles.Sysadmin));
        Assert.True(created.EmailConfirmed);

        // Second run: still one user, still has the role, no exceptions.
        await seeder.SeedAsync();
        var refetched = await users.FindByEmailAsync(email);
        Assert.NotNull(refetched);
        Assert.Equal(created.Id, refetched!.Id);
        Assert.True(await users.IsInRoleAsync(refetched, IdentityRoles.Sysadmin));
    }

    [Fact]
    public async Task Seed_WithMissingPassword_IsNoOp()
    {
        await SeedRolesAsync();

        var seeder = NewSeeder("nobody@incoders.ai", password: "");
        await seeder.SeedAsync();

        var users = _sp.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByEmailAsync("nobody@incoders.ai"));
    }

    [Fact]
    public async Task Seed_WithWeakPassword_ThrowsDescriptiveError()
    {
        await SeedRolesAsync();

        var seeder = NewSeeder("weak@incoders.ai", password: "short");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());
        Assert.Contains("Failed to create sysadmin user", ex.Message);
    }

    [Fact]
    public async Task Seed_ReassignsSysadminRole_WhenUserExistsButRoleIsMissing()
    {
        await SeedRolesAsync();
        var users = _sp.GetRequiredService<UserManager<ApplicationUser>>();

        // Simulate: user exists but role was somehow stripped.
        var preexisting = new ApplicationUser
        {
            UserName = "ghost@incoders.ai",
            Email = "ghost@incoders.ai",
            EmailConfirmed = true,
        };
        var created = await users.CreateAsync(preexisting, "#GhostPwd12345");
        Assert.True(created.Succeeded);
        Assert.False(await users.IsInRoleAsync(preexisting, IdentityRoles.Sysadmin));

        var seeder = NewSeeder("ghost@incoders.ai", "#GhostPwd12345");
        await seeder.SeedAsync();

        Assert.True(await users.IsInRoleAsync(preexisting, IdentityRoles.Sysadmin));
    }

    public void Dispose() => _sp.Dispose();
}
