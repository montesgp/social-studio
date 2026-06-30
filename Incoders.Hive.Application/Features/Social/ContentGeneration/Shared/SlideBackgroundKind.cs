namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Classifies the background kind of a single carousel slide as captured from
/// a client's real published feed during aesthetic extraction.
/// Used to build the <c>BackgroundPattern</c> sequence on <see cref="ClientAestheticProfile"/>.
/// Pure Application value object — no provider/EF/Infrastructure types.
/// Refs: design §3.1 OQ-1; CAC-BSP-01.
/// </summary>
public enum SlideBackgroundKind
{
    /// <summary>A photographic background (generated or conditioned from a real creative image).</summary>
    Photo,

    /// <summary>A flat solid-color or minimal-gradient background.</summary>
    FlatColor,

    /// <summary>A decorative / template-overlay background (illustrations, patterns, etc.).</summary>
    Decorator,
}
