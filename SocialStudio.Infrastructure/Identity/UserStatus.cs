using System.Text.Json.Serialization;

namespace SocialStudio.Infrastructure.Identity;

/// <summary>
/// Lifecycle status of an <see cref="ApplicationUser"/>.
/// Extend with additional states (e.g. SuspendedForPayment) as the product evolves.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserStatus
{
    Active = 0,
    Suspended = 1,
}
