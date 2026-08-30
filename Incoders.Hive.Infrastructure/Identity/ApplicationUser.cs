using Microsoft.AspNetCore.Identity;

namespace Incoders.Hive.Infrastructure.Identity;

/// <summary>
/// Identity user keyed by <see cref="Guid"/>. Extend with profile fields as the product evolves.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }

    /// <summary>
    /// Lifecycle status. Suspended users are denied login regardless of credentials.
    /// Defaults to <see cref="UserStatus.Active"/>.
    /// </summary>
    public UserStatus Status { get; set; } = UserStatus.Active;
}
