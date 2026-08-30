using System.Text.Json;
using System.Text.Json.Serialization;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

/// <summary>
/// Serialization helper for <c>composition_spec_json</c>. A single-asset format (e.g. story) stores
/// ONE envelope; a multi-asset format (carousel) stores ONE envelope PER slide so every slide stays
/// editable on export. To stay backward-compatible with rows written before multi-slide support,
/// a single envelope is serialized as a JSON object and many as a JSON array; the parser accepts
/// both shapes.
/// </summary>
public static class CompositionSpecJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes one-or-many envelopes. A single envelope is written as a JSON object (legacy
    /// shape); multiple as a JSON array. Returns null for an empty list.
    /// </summary>
    public static string? Serialize(IReadOnlyList<CompositionSpecEnvelope> envelopes)
    {
        if (envelopes.Count == 0) return null;
        return envelopes.Count == 1
            ? JsonSerializer.Serialize(envelopes[0], Options)
            : JsonSerializer.Serialize(envelopes, Options);
    }

    /// <summary>
    /// Parses and returns only the ACTIVE envelopes — those not tombstoned via
    /// <see cref="CompositionSpecEnvelope.Deleted"/>. Render/export paths use this so a
    /// slide the user deleted never appears in a PPTX / Canva / ZIP deliverable, while the
    /// tombstone is preserved on disk (positions are not renumbered). See
    /// <see cref="ParseList"/> when the full list (including tombstones) is required.
    /// </summary>
    public static IReadOnlyList<CompositionSpecEnvelope> ParseActiveList(string? json) =>
        ParseList(json).Where(e => !e.Deleted).ToList();

    /// <summary>
    /// Parses <c>composition_spec_json</c> into a list of envelopes, accepting both the legacy
    /// single-object shape and the multi-slide array shape. Returns an empty list on
    /// null/blank/unparseable input (callers treat that as "not exportable").
    /// Tombstoned envelopes are INCLUDED so positional slide indices stay stable; use
    /// <see cref="ParseActiveList"/> for render/export.
    /// </summary>
    public static IReadOnlyList<CompositionSpecEnvelope> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        var trimmed = json.TrimStart();
        try
        {
            if (trimmed.StartsWith('['))
            {
                return JsonSerializer.Deserialize<List<CompositionSpecEnvelope>>(json, Options) ?? [];
            }

            var single = JsonSerializer.Deserialize<CompositionSpecEnvelope>(json, Options);
            return single is null ? [] : [single];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

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
/// Reference to the background-only PNG. For new generations (studio-background-asset-ref,
/// Slice 1 / BGREF-01) this is a storage path resolved via <c>IGeneratedAssetStorage</c> once
/// storage is wired. Rows persisted before that change may still carry a legacy
/// <c>data:image/...;base64,...</c> URI — export/render consumers MUST resolve both forms
/// (BGREF-03). Null until a background is captured.
/// </param>
/// <param name="FlattenedImageRef">
/// Data-URI for the FULL flattened render (background + decorations + text) produced when the
/// Content Studio saves a visual edit. When present, the PPTX export uses it as the slide image and
/// skips editable text boxes, so the export is pixel-faithful to whatever the studio shows. Null
/// until flattened. Additive — envelopes written before this field deserialize it as null.
/// </param>
/// <param name="Deleted">
/// Tombstone flag. When true, the user deleted this slide from the package. The envelope is kept
/// in place (slides are NOT renumbered) so remaining slide indices stay stable, but render/export
/// paths skip it via <see cref="CompositionSpecJson.ParseActiveList"/>. Additive — envelopes written
/// before this field deserialize it as false.
/// </param>
/// <param name="DegradeReason">
/// D2 (design decision): set to a verbatim token (see
/// <c>Shared.SlideDegradeReason</c>) when this slide rendered through the deterministic
/// faithful-flat degrade path instead of its normal designer/scrim-card path. Null for every
/// other slide. <c>[JsonIgnore(Condition = WhenWritingNull)]</c> keeps a non-degraded envelope's
/// serialized JSON byte-identical to its pre-change form — no key is written at all when null.
/// Additive — envelopes written before this field deserialize it as null.
/// </param>
public sealed record CompositionSpecEnvelope(
    int SchemaVersion,
    string Markup,
    CanvasDimensions Canvas,
    IReadOnlyList<SlotGeometryEntry> SlotGeometry,
    string? BackgroundImageRef = null,
    string? FlattenedImageRef = null,
    bool Deleted = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DegradeReason = null)
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
/// <param name="FontWeight">Computed <c>font-weight</c> (e.g. <c>"700"</c>). Null if not measured. Optional (additive, schema v1-compatible).</param>
/// <param name="FontStyle">Computed <c>font-style</c> (<c>"italic"</c>/<c>"normal"</c>). Null if not measured.</param>
/// <param name="LineHeightPx">Computed <c>line-height</c> in px. 0 when <c>normal</c>/not measured.</param>
/// <param name="LetterSpacingPx">Computed <c>letter-spacing</c> in px. 0 when <c>normal</c>/not measured.</param>
/// <param name="TextTransform">Computed <c>text-transform</c> (e.g. <c>"uppercase"</c>). Null if not measured.</param>
public sealed record SlotGeometryEntry(
    string Slot,
    double X,
    double Y,
    double Width,
    double Height,
    string? FontFamily,
    double FontSizePx,
    string? Color,
    string? TextAlign,
    string? FontWeight = null,
    string? FontStyle = null,
    double LineHeightPx = 0,
    double LetterSpacingPx = 0,
    string? TextTransform = null);
