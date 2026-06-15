using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

/// <summary>An asset reference supplied by a visual edit (image replacement / background swap).</summary>
public sealed record VisualAssetInput(string Role, string StorageUri, string Mime);

/// <summary>
/// Edits the visual draft of a piece: composition markup + asset references (image replacement,
/// background swap, basic layout/branding adjustments). Sets the item to Edited. The immutable
/// source result is never touched.
///
/// <para>
/// Step 2 (fidelity): when a geometry-capable renderer is available, the edited markup is re-rendered
/// server-side and the FULL flattened PNG is stored on each envelope (<c>FlattenedImageRef</c>). The
/// PPTX export then uses that image as the slide, so ANY edit — text, fonts, colors, moves AND
/// non-slot decorations/lines/buttons — is faithful to what the studio shows. The studio becomes the
/// source of truth for the package. Re-render is best-effort: any failure degrades to saving the
/// markup as-is (the legacy background + text-box export).
/// </para>
/// </summary>
public sealed record EditItemVisualCommand(
    Guid OrganizationId,
    Guid ItemId,
    string? VisualSpecMarkup,
    IReadOnlyList<VisualAssetInput> Assets) : IRequest<Result<ContentItemResponse>>;

public sealed class EditItemVisualCommandHandler
    : IRequestHandler<EditItemVisualCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;
    private readonly IReadOnlyList<IContentRenderer> _renderers;

    public EditItemVisualCommandHandler(
        IContentItemRepository items,
        IClock clock,
        IEnumerable<IContentRenderer>? renderers = null)
    {
        _items = items;
        _clock = clock;
        _renderers = renderers?.ToList() ?? [];
    }

    public async Task<Result<ContentItemResponse>> Handle(EditItemVisualCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var assets = (request.Assets ?? [])
            .Select(a => new ContentAssetRef(a.Role, a.StorageUri, a.Mime))
            .ToList();

        // Re-render the edited markup so the export stays faithful (best-effort; see class remarks).
        var markup = await FlattenEditsAsync(request.VisualSpecMarkup, ct);

        var result = item.EditVisual(markup, assets, _clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(result.Error);
        }

        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }

    /// <summary>
    /// Re-renders every envelope's edited markup to a full flattened PNG (stored as
    /// <c>FlattenedImageRef</c>). Returns the original markup unchanged when no geometry renderer is
    /// wired, the markup is not a composition envelope, or rendering fails.
    /// </summary>
    private async Task<string?> FlattenEditsAsync(string? visualSpecMarkup, CancellationToken ct)
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
            // Never fail a save because of a render hiccup — degrade to the legacy export path.
            return null;
        }
    }
}
