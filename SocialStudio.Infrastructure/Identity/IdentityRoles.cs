namespace SocialStudio.Infrastructure.Identity;

/// <summary>
/// Canonical role names persisted in <c>identity.roles</c> and asserted in <c>role</c> claims.
/// </summary>
public static class IdentityRoles
{
    /// <summary>
    /// Platform-wide administrator. Sees all Organizations and manages global catalogs (AI providers,
    /// AI models, publication providers, system settings).
    /// </summary>
    public const string Sysadmin = "sysadmin";

    /// <summary>
    /// Organization-scoped administrator. Manages clients, social accounts, members and integrations
    /// inside their Organization. Cannot see other Organizations.
    /// </summary>
    public const string Admin = "admin";

    /// <summary>
    /// Convenience enumeration of all roles seeded at startup.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Sysadmin, Admin];
}
