namespace Incoders.Hive.Application.Abstractions;

/// <summary>
/// Exposes the caller's identity to the application layer without coupling to HTTP or Identity frameworks.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Email { get; }

    /// <summary>
    /// True when the caller has platform-level system administration privileges.
    /// Sourced from <c>app_metadata.is_platform_admin</c> in the Supabase JWT.
    /// Set server-side via the Supabase Admin API — not self-assignable by users.
    /// </summary>
    bool IsPlatformAdmin { get; }
}
