using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

/// <summary>
/// Re-renders a studio item's edited markup into a full flattened PNG per envelope
/// (<c>FlattenedImageRef</c>), so the PPTX export reproduces exactly what the studio shows —
/// including non-slot decorations, lines and buttons the legacy background+text-box export lost.
///
/// <para>
/// This used to live inside <see cref="EditItemVisualCommandHandler"/> and run INSIDE the save
/// request. It drives headless Chromium once per envelope, so a five-slide carousel meant five
/// sequential renders behind a two-slot semaphore shared with content generation — long enough to
/// outlive the dev proxy's socket and surface as "Failed to fetch" on a save. It is now a separate
/// unit so the save can persist immediately and a background worker can do this afterwards.
/// </para>
///
/// <para>Every failure degrades to the original markup: a preview is never worth losing an edit.</para>
/// </summary>
public sealed class VisualFlattener
{
    private readonly IReadOnlyList<IContentRenderer> _renderers;

    public VisualFlattener(IEnumerable<IContentRenderer>? renderers = null) =>
        _renderers = renderers?.ToList() ?? [];

    /// <summary>True when a geometry-capable renderer is wired; otherwise flattening is a no-op.</summary>
    public bool CanFlatten => _renderers.OfType<IContentRendererWithGeometry>().Any();

    /// <summary>
    /// Returns the markup with each envelope's <c>FlattenedImageRef</c> refreshed, or the input
    /// unchanged when no renderer is wired, the markup is not a composition envelope, or rendering
    /// fails.
    /// </summary>
    public async Task<string?> FlattenAsync(string? visualSpecMarkup, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(visualSpecMarkup))
        {
            return visualSpecMarkup;
        }

        if (_renderers.OfType<IContentRendererWithGeometry>().FirstOrDefault() is not { } renderer)
        {
            return visualSpecMarkup;
        }

        var envelopes = CompositionSpecJson.ParseList(visualSpecMarkup);
        if (envelopes.Count == 0)
        {
            return visualSpecMarkup;
        }

        var flattened = new List<CompositionSpecEnvelope>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            flattened.Add(await TryFlattenAsync(renderer, envelope, ct) ?? envelope);
        }

        return CompositionSpecJson.Serialize(flattened) ?? visualSpecMarkup;
    }

    private static async Task<CompositionSpecEnvelope?> TryFlattenAsync(
        IContentRendererWithGeometry renderer,
        CompositionSpecEnvelope envelope,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envelope.Markup))
        {
            return null;
        }

        try
        {
            var spec = new VisualCompositionSpec(
                FormatKey: "studio-edit",
                LayoutType: "html",
                Palette: new CompositionPalette("#000000", "#000000", "#000000", "#000000", "#FFFFFF"),
                Typography: new CompositionTypography("sans-serif", 48, "sans-serif", 24),
                AssetSlots: new Dictionary<string, AssetSlotSpec>(),
                CopySlots: new Dictionary<string, string>(),
                Dimensions: new CompositionDimensions(envelope.Canvas.Width, envelope.Canvas.Height, envelope.Canvas.Dpi))
            {
                OutputKind = CompositionOutputKind.Html,
                DesignerMarkup = envelope.Markup,
            };

            var rendered = await renderer.RenderWithGeometryAsync(spec, new RenderOptions(), ct);
            if (!rendered.IsSuccess)
            {
                return null;
            }

            var bytes = rendered.Value.Asset.GetBytes();
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            var dataUri = $"data:{rendered.Value.Asset.Mime};base64,{Convert.ToBase64String(bytes)}";

            return envelope with
            {
                FlattenedImageRef = dataUri,
                SlotGeometry = rendered.Value.SlotGeometry.Count > 0
                    ? rendered.Value.SlotGeometry
                    : envelope.SlotGeometry,
            };
        }
        catch
        {
            // Never fail because of a render hiccup — degrade to the legacy export path.
            return null;
        }
    }
}
