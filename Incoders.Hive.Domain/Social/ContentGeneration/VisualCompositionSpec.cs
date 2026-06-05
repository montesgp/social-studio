namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Signals to the renderer whether the composition should be emitted as
/// a raster image (PNG), a plain-text document (.txt), or a Markdown document (.md).
/// The renderer dispatches on this enum and MUST NOT branch on format key names.
/// </summary>
public enum CompositionOutputKind
{
    /// <summary>Default. Rendered to PNG via the raster pipeline.</summary>
    Raster,

    /// <summary>Plain-text output. Renderer emits UTF-8 .txt bytes from <see cref="VisualCompositionSpec.TextBody"/>.</summary>
    Text,

    /// <summary>
    /// Markdown output. Renderer emits UTF-8 .md bytes from <see cref="VisualCompositionSpec.TextBody"/>
    /// with MIME type <c>text/markdown; charset=utf-8</c> and filename <c>{FormatKey}.md</c>.
    /// </summary>
    Markdown,
}

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
    CompositionDimensions Dimensions)
{
    /// <summary>
    /// Signals the renderer to emit a .txt or .md file instead of a .png.
    /// Defaults to <see cref="CompositionOutputKind.Raster"/> for backward compatibility.
    /// </summary>
    public CompositionOutputKind OutputKind { get; init; } = CompositionOutputKind.Raster;

    /// <summary>
    /// The plain-text body to emit when <see cref="OutputKind"/> is <see cref="CompositionOutputKind.Text"/>.
    /// Set by text-only composers from their CopySlots. Null for raster formats.
    /// </summary>
    public string? TextBody { get; init; }
}

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
