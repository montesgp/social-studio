namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Deterministic visual composition specification for a single format.
/// Produced by <c>IVisualCompositionService</c> and consumed by <c>IContentRenderer</c>.
/// Decouples composition reasoning from rendering mechanics.
/// </summary>
public sealed record VisualCompositionSpec(
    string FormatKey,
    string LayoutType,
    CompositionPalette Palette,
    CompositionTypography Typography,
    IReadOnlyDictionary<string, AssetSlotSpec> AssetSlots,
    IReadOnlyDictionary<string, string> CopySlots,
    CompositionDimensions Dimensions);

/// <summary>Brand-derived or default colour palette.</summary>
public sealed record CompositionPalette(
    string Primary,
    string Secondary,
    string Accent,
    string Text,
    string Background);

/// <summary>Typography settings for headings and body copy.</summary>
public sealed record CompositionTypography(
    string HeadingFamily,
    int HeadingSize,
    string BodyFamily,
    int BodySize);

/// <summary>
/// A named image slot — either a resolved URL or a placeholder hint for the renderer.
/// </summary>
public sealed record AssetSlotSpec(
    string? ImageUrl,
    string? PlaceholderHint);

/// <summary>Target raster output dimensions.</summary>
public sealed record CompositionDimensions(
    int Width,
    int Height,
    int Dpi);
