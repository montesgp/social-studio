using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SocialStudio.Infrastructure.Identity;

public sealed class SysAdminSeederOptions
{
    public const string SectionName = "SeedUsers:SysAdmin";

    public string Email { get; set; } = "sysadmin@incoders.ai";

    /// <summary>
    /// Must be supplied via user-secrets or environment variable
    /// (e.g. <c>SEEDUSERS__SYSADMIN__PASSWORD</c>). Never commit this value.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
}

/// <summary>
/// Ensures a platform sysadmin user exists at application startup, persisted by ASP.NET Identity
/// in the <c>identity</c> schema and assigned the <see cref="IdentityRoles.Sysadmin"/> role.
/// Idempotent: skips creation when the user already exists, and re-attaches the role if missing.
/// </summary>
public sealed class SysAdminUserSeeder
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SysAdminSeederOptions _opts;
    private readonly ILogger<SysAdminUserSeeder> _logger;

    public SysAdminUserSeeder(
        UserManager<ApplicationUser> users,
        IOptions<SysAdminSeederOptions> options,
        ILogger<SysAdminUserSeeder> logger)
    {
        _users = users;
        _opts = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_opts.Password))
        {
            _logger.LogWarning(
                "SysAdmin seeder skipped: {Section}:Password is not configured. " +
                "Set it via user-secrets or the SEEDUSERS__SYSADMIN__PASSWORD environment variable.",
                SysAdminSeederOptions.SectionName);
            return;
        }

        var existing = await _users.FindByEmailAsync(_opts.Email);
        if (existing is null)
        {
            var user = new ApplicationUser
            {
                UserName = _opts.Email,
                Email = _opts.Email,
                EmailConfirmed = true,
                DisplayName = _opts.DisplayName ?? "Platform Sysadmin",
            };

            var create = await _users.CreateAsync(user, _opts.Password);
            if (!create.Succeeded)
            {
                var errors = string.Join("; ", create.Errors.Select(e => $"{e.Code}:{e.Description}"));
                throw new InvalidOperationException($"Failed to create sysadmin user '{_opts.Email}': {errors}");
            }

            _logger.LogInformation("Created platform sysadmin user: {Email}", _opts.Email);
            existing = user;
        }
        else
        {
            _logger.LogDebug("Platform sysadmin user already exists: {Email}", existing.Email);
        }

        if (!await _users.IsInRoleAsync(existing, IdentityRoles.Sysadmin))
        {
            var assign = await _users.AddToRoleAsync(existing, IdentityRoles.Sysadmin);
            if (!assign.Succeeded)
            {
                var errors = string.Join("; ", assign.Errors.Select(e => $"{e.Code}:{e.Description}"));
                throw new InvalidOperationException(
                    $"Failed to assign '{IdentityRoles.Sysadmin}' role to '{_opts.Email}': {errors}");
            }

            _logger.LogInformation("Assigned {Role} role to {Email}", IdentityRoles.Sysadmin, existing.Email);
        }
    }
}
