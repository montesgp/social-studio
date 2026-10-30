using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;

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
        var copySlots = BuildCopySlots(generatedPayload);
        var textBody = string.Join("\n\n", copySlots.Values.Where(v => !string.IsNullOrWhiteSpace(v)));

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: "text-card",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                [SlotKey] = new AssetSlotSpec(null, null)
            },
            CopySlots: copySlots,
            Dimensions: CompositionDefaults.StandardDimensions)
        {
            OutputKind = CompositionOutputKind.Text,
            TextBody = textBody,
        };
    }

    protected virtual IReadOnlyDictionary<string, string> BuildCopySlots(object generatedPayload) =>
        new Dictionary<string, string>
        {
            ["text"] = "Generated text content"
        };
}
