using SocialStudio.Domain.Social.ContentStudio;

namespace SocialStudio.Domain.Social.ContentFormats;

/// <summary>
/// An immutable row in the <see cref="FormatCatalog"/>. Describes a single supported
/// content format in terms of its taxonomy axes, channel placement, asset behavior,
/// and display metadata. Instances are only created by the catalog; callers obtain them
/// through catalog lookups.
/// </summary>
public sealed record FormatDefinition(
    string        FormatKey,
    Surface       Surface,
    Layout?       Layout,
    Medium        Medium,
    TargetChannel Channel,
    ContentMedium ContentMedium,
    bool          IsMultiAsset,
    string        DisplayLabel,
    string        StorageFolder)
{
    /// <summary>
    /// True when the format produces visual raster assets (Image or Video medium).
    /// Used to gate visual-composition and raster-generation pipeline paths.
    /// </summary>
    public bool IsVisual => Medium is Medium.Image or Medium.Video;

    /// <summary>
    /// True for auxiliary/support formats (content-idea, cta-variation, caption, hook)
    /// that are included in the registry but excluded from the user-facing surface picker.
    /// </summary>
    public bool IsAuxiliary { get; init; }
}
