using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Extended renderer port that captures per-<c>data-slot</c> bounding-rect geometry
/// during the existing render pass and returns it alongside the rendered asset.
///
/// <para>
/// Implemented by <c>HtmlChromiumContentRenderer</c> (B3 D3). Infrastructure adapters that
/// cannot measure geometry (e.g. <c>LocalSvgContentRenderer</c>) do NOT implement this interface —
/// the pipeline falls back to rendering without geometry capture, resulting in an empty
/// <see cref="SlotGeometry"/> list in the persisted envelope.
/// </para>
///
/// <para>
/// This interface is provider-free: parameter and return types use only Domain records
/// and Application data contracts — no Playwright, no OpenXML, no ASP.NET types.
/// </para>
/// </summary>
public interface IContentRendererWithGeometry : IContentRenderer
{
    /// <summary>
    /// Renders the spec (identical to <see cref="IContentRenderer.RenderAsync"/>) and additionally
    /// captures <c>getBoundingClientRect</c> geometry for every <c>[data-slot]</c> element found
    /// during the same page session.
    /// </summary>
    /// <param name="spec">The composition spec to render. Must have <c>OutputKind = Html</c>.</param>
    /// <param name="opts">Rendering options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A successful <see cref="RenderedAssetWithGeometry"/> when rendering succeeded;
    /// a failed result carrying the same error codes as <see cref="IContentRenderer.RenderAsync"/>.
    /// </returns>
    Task<Result<RenderedAssetWithGeometry>> RenderWithGeometryAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct);
}

/// <summary>
/// A rendered asset paired with the per-slot geometry captured during the same render pass (D3).
/// </summary>
/// <param name="Asset">The rendered PNG asset.</param>
/// <param name="SlotGeometry">
/// Geometry entries for each <c>[data-slot]</c> element found in the markup.
/// Empty when no slots were present or geometry capture failed gracefully.
/// </param>
public sealed record RenderedAssetWithGeometry(
    RenderedAsset Asset,
    IReadOnlyList<SlotGeometryEntry> SlotGeometry);
