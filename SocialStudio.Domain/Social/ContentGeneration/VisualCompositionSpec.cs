namespace SocialStudio.Domain.Social.ContentGeneration;

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

    /// <summary>
    /// HTML/CSS composition authored by <c>IVisualDesigner</c> and validated by the guardrail pipeline.
    /// Renderer dispatches on this kind and renders <see cref="VisualCompositionSpec.DesignerMarkup"/>
    /// via headless Chromium (B1b) or falls back to legacy slot rendering (SkiaSharp fallback).
    /// Added in B1a alongside <see cref="VisualCompositionSpec.DesignerMarkup"/> (Design Decision 12).
    /// </summary>
    Html,
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

    /// <summary>
    /// Validated HTML markup produced by <c>IVisualDesigner</c> and passed through the guardrail pipeline.
    /// Set by the pipeline when <see cref="OutputKind"/> is <see cref="CompositionOutputKind.Html"/>.
    /// Null for raster/text/markdown formats. (Design Decision 12 — additive, default null.)
    /// </summary>
    public string? DesignerMarkup { get; init; }

    /// <summary>
    /// client-style-fidelity (CSF-CG-06): copy-slot name → the exact substring of that slot's text the
    /// client would emphasise in <see cref="HighlightColor"/>. Null when nothing is highlighted.
    /// </summary>
    public IReadOnlyDictionary<string, string>? TextHighlights { get; init; }

    /// <summary>
    /// client-style-fidelity (CSF-CG-06): the highlight colour OBSERVED in the client's posts for this
    /// slide's colour pairing. Null when the client does not highlight words, which disables
    /// <see cref="TextHighlights"/>.
    /// </summary>
    public string? HighlightColor { get; init; }
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
    int BodySize)
{
    /// <summary>CSS numeric weight for headings. Additive with an init default, so every existing
    /// positional construction site keeps compiling and keeps its previous rendering.</summary>
    public int HeadingWeight { get; init; } = 700;

    /// <summary>
    /// The client's REAL observed heading weight, when a <c>ClientAestheticProfile</c> measured one.
    /// Null when unmeasured (cold-start / no profile) — unlike <see cref="HeadingWeight"/>, which
    /// always carries a value (a non-nullable int with an init default), this nullable twin lets a
    /// markup builder distinguish "the client's font really is this weight" from "no signal, use my
    /// own pre-existing hardcoded literal" — the same pattern <see cref="MeasuredHeadingSize"/> already
    /// established for size. Byte-identical cold-start parity depends on builders consulting THIS
    /// field (not <see cref="HeadingWeight"/>) before falling back to their literal.
    /// </summary>
    public int? MeasuredHeadingWeight { get; init; }

    /// <summary>CSS numeric weight for body copy.</summary>
    public int BodyWeight { get; init; } = 400;

    /// <summary>True when body copy is set in italics (a serif-italic accent voice, for instance).</summary>
    public bool BodyItalic { get; init; }

    /// <summary>
    /// Family the CLIENT actually uses for headings, when it differs from <see cref="HeadingFamily"/>
    /// because no binary for it is bundled. Null when the rendered family is the real one. Purely
    /// diagnostic — it lets a report say "substituted X for Y" instead of silently drawing X.
    /// </summary>
    public string? RequestedHeadingFamily { get; init; }

    /// <summary>Body counterpart of <see cref="RequestedHeadingFamily"/>.</summary>
    public string? RequestedBodyFamily { get; init; }

    /// <summary>
    /// Family the client uses for the caption/overline role (small emphasised copy — e.g. a CTA or
    /// signature line). Null falls back to <see cref="BodyFamily"/> via <see cref="EffectiveCaptionFamily"/>
    /// — additive with an init default, so every existing positional construction site keeps compiling.
    /// </summary>
    public string? CaptionFamily { get; init; }

    /// <summary>Observed caption size in pixels. Null keeps the builder's existing hardcoded size.</summary>
    public int? CaptionSize { get; init; }

    /// <summary>CSS numeric weight for the caption role. Defaults to today's hardcoded cta weight.</summary>
    public int CaptionWeight { get; init; } = 800;

    /// <summary>True when the caption/overline role is consistently italic in the client's posts.</summary>
    public bool CaptionItalic { get; init; }

    /// <summary>Caption counterpart of <see cref="RequestedHeadingFamily"/>.</summary>
    public string? RequestedCaptionFamily { get; init; }

    /// <summary>The family actually used to render the caption role — falls back to <see cref="BodyFamily"/> when unmeasured.</summary>
    public string EffectiveCaptionFamily => CaptionFamily ?? BodyFamily;

    /// <summary>
    /// Observed heading size in pixels, derived from the client's measured <c>SizeRatio</c>.
    /// Null keeps the builder's own hardcoded heading literal (client-aesthetic-generation-hardening,
    /// D1) — mirrors the existing <see cref="CaptionSize"/> nullable-fallback pattern. Deliberately
    /// distinct from <see cref="HeadingSize"/>, which is non-nullable and already defaults to 28px for
    /// every unmeasured client; reading that value directly here would shrink a cold-start headline.
    /// </summary>
    public int? MeasuredHeadingSize { get; init; }

    /// <summary>Body counterpart of <see cref="MeasuredHeadingSize"/> (D1).</summary>
    public int? MeasuredBodySize { get; init; }

    /// <summary>
    /// client-style-fidelity (CSF-CG-04): the client's observed body weight, clamped to the resolved
    /// family. Null keeps each builder's own body weight literal (cold-start parity).
    /// </summary>
    public int? MeasuredBodyWeight { get; init; }

    /// <summary>
    /// client-style-fidelity (CSF-CG-05): headline block width in pixels, measured from the client's
    /// slides. Null keeps each builder's own width literal (cold-start parity).
    /// </summary>
    public int? MeasuredHeadingBlockWidth { get; init; }
}

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
