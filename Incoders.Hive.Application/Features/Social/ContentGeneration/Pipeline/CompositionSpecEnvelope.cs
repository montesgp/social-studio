namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

/// <summary>
/// Versioned JSON envelope persisted in <c>social.content_generation_format_results.composition_spec_json</c>.
///
/// <para>
/// D1 (design decision): the column was already mapped end-to-end but written <c>null</c> in every
/// production path. B3 WU-1 fixes the write site to store this envelope on every successful render.
/// </para>
///
/// <para>
/// D3 (design decision): slot geometry is captured via Playwright <c>getBoundingClientRect</c>
/// during the existing Chromium render pass and stored here alongside the markup so the PPTX
/// adapter never needs to launch a browser.
/// </para>
///
/// <para>
/// <c>schemaVersion = 1</c> is the initial version. Consumers MUST reject unknown versions
/// rather than silently misinterpreting them.
/// </para>
/// </summary>
/// <param name="SchemaVersion">
/// Envelope schema version. Current value: <c>1</c>.
/// </param>
/// <param name="Markup">
/// Guardrail-validated HTML markup produced by <c>IVisualDesigner</c>. Non-null for every
/// successfully rendered designed-raster format.
/// </param>
/// <param name="Canvas">
/// The canvas dimensions used during rendering. Drives PPTX slide size.
/// </param>
/// <param name="SlotGeometry">
/// Per-<c>data-slot</c> bounding rects measured by Playwright at render time.
/// Empty list when geometry capture was not available (e.g. fallback renderer).
/// </param>
/// <param name="BackgroundImageRef">
/// Storage reference for the background-only PNG (future WU-2+). Null until captured.
/// </param>
public sealed record CompositionSpecEnvelope(
    int SchemaVersion,
    string Markup,
    CanvasDimensions Canvas,
    IReadOnlyList<SlotGeometryEntry> SlotGeometry,
    string? BackgroundImageRef = null)
{
    /// <summary>Current envelope schema version.</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Canvas dimensions from the composition spec — drives PPTX slide size.</summary>
/// <param name="Width">Width in logical pixels.</param>
/// <param name="Height">Height in logical pixels.</param>
/// <param name="Dpi">Dots per inch (default 96). Used for px→EMU conversion.</param>
public sealed record CanvasDimensions(int Width, int Height, int Dpi);

/// <summary>
/// Bounding rect and computed text style for a single <c>data-slot</c> element,
/// as measured by <c>getBoundingClientRect</c> at render time.
///
/// <para>Coordinates are in logical CSS pixels (pre-DeviceScaleFactor).</para>
/// </summary>
/// <param name="Slot">The <c>data-slot</c> attribute value (e.g. <c>"headline"</c>).</param>
/// <param name="X">Left edge in CSS pixels.</param>
/// <param name="Y">Top edge in CSS pixels.</param>
/// <param name="Width">Element width in CSS pixels.</param>
/// <param name="Height">Element height in CSS pixels.</param>
/// <param name="FontFamily">Computed <c>font-family</c> (first token). Null if not measurable.</param>
/// <param name="FontSizePx">Computed <c>font-size</c> in px. 0 if not measurable.</param>
/// <param name="Color">Computed <c>color</c> (CSS color string). Null if not measurable.</param>
/// <param name="TextAlign">Computed <c>text-align</c>. Null if not measurable.</param>
public sealed record SlotGeometryEntry(
    string Slot,
    double X,
    double Y,
    double Width,
    double Height,
    string? FontFamily,
    double FontSizePx,
    string? Color,
    string? TextAlign);
