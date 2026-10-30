using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port — converts a <see cref="CompositionSpecEnvelope"/>
/// into an editable PPTX presentation (<c>byte[]</c>).
///
/// <para>
/// The adapter is 100% offline (D3): it consumes the markup and per-slot geometry that were
/// captured at render time and persisted in <c>composition_spec_json</c>. It MUST NOT launch
/// Chromium, make network calls, or touch AI models at export time.
/// </para>
///
/// <para>
/// Mapping rules (from design):
/// <list type="bullet">
///   <item><description><c>[data-slot]</c> elements → editable text boxes (<c>Shape</c> + <c>TextBody</c>)
///     positioned using persisted geometry converted to EMU.</description></item>
///   <item><description><c>[data-layer="background"]</c> → full-bleed <c>Picture</c> layer behind text boxes,
///     sourced from <c>backgroundImageRef</c> (data-URI inline PNG or storage ref).</description></item>
///   <item><description>Slide dimensions derived from <c>envelope.Canvas</c> in EMU (px × 9525 at 96 dpi).</description></item>
/// </list>
/// </para>
///
/// <para>
/// Spec: PPTX-02 (editable text boxes, background layer, correct dimensions),
/// PPTX-03 (deterministic, offline).
/// </para>
/// </summary>
public interface IPptxExportAdapter
{
    /// <summary>
    /// Builds an editable PPTX from the given <see cref="CompositionSpecEnvelope"/>.
    /// </summary>
    /// <param name="envelope">
    /// The versioned envelope persisted in <c>composition_spec_json</c>.
    /// Must have <c>SchemaVersion = 1</c>. Must not be null.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A successful <c>Result</c> carrying the raw PPTX <c>byte[]</c> on success;
    /// a failed <c>Result</c> with an error key (e.g. <c>"pptx.unsupported-schema-version"</c>)
    /// when the envelope is unsupported or malformed.
    /// </returns>
    Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// Builds a single editable PPTX with one slide per envelope — used for multi-slide formats
    /// (carousel) and multi-format packages. All envelopes are assumed to share the canvas size of
    /// the first (PPTX has a single presentation-wide slide size).
    /// </summary>
    /// <param name="envelopes">Ordered envelopes; each becomes one slide. Must be non-empty.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<byte[]>> BuildManyAsync(IReadOnlyList<CompositionSpecEnvelope> envelopes, CancellationToken ct);
}
