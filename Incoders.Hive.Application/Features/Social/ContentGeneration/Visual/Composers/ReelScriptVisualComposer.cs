using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ReelScript;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class ReelScriptVisualComposer : IFormatVisualComposer
{
    public string FormatKey => "reel-script";

    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var payload = PayloadReader.As<ReelScriptContent>(generatedPayload);
        var palette = CompositionDefaults.ResolvePalette(brand, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);
        var scenes = payload is null
            ? string.Empty
            : string.Join(" ", payload.Scenes.Select(scene => $"{scene.Timestamp}: {scene.Voiceover} ({scene.OnScreenText})"));

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: "text-card",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["script-document"] = new AssetSlotSpec(null, null)
            },
            CopySlots: new Dictionary<string, string>
            {
                ["script"] = payload is null
                    ? "Reel script content"
                    : $"{payload.Hook} {scenes} CTA: {payload.CtaText}"
            },
            Dimensions: CompositionDefaults.StandardDimensions);
    }
}
