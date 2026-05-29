using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

/// <summary>
/// Shared base for text-only format composers (hook, caption, cta-variation).
/// Produces a single text-card asset slot so the renderer can generate a styled PNG text card.
/// </summary>
internal abstract class TextCardVisualComposerBase : IFormatVisualComposer
{
    public abstract string FormatKey { get; }

    protected abstract string SlotKey { get; }

    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var palette = CompositionDefaults.ResolvePalette(brand, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: "text-card",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                [SlotKey] = new AssetSlotSpec(null, null)
            },
            CopySlots: BuildCopySlots(generatedPayload),
            Dimensions: CompositionDefaults.StandardDimensions);
    }

    protected virtual IReadOnlyDictionary<string, string> BuildCopySlots(object generatedPayload) =>
        new Dictionary<string, string>
        {
            ["text"] = "Generated text content"
        };
}
