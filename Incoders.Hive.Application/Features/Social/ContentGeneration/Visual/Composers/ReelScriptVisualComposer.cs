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

        string scriptBody;
        if (payload is null)
        {
            scriptBody = "Reel script content";
        }
        else
        {
            var sceneBlocks = payload.Scenes.Select(scene =>
            {
                var line = $"**{scene.Timestamp}**: {scene.Voiceover}";
                return string.IsNullOrWhiteSpace(scene.OnScreenText)
                    ? line
                    : $"{line}\n{scene.OnScreenText}";
            });

            var scenesJoined = string.Join("\n\n", sceneBlocks);

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(payload.Hook))
            {
                parts.Add(payload.Hook);
            }
            if (!string.IsNullOrWhiteSpace(scenesJoined))
            {
                parts.Add(scenesJoined);
            }
            if (!string.IsNullOrWhiteSpace(payload.CtaText))
            {
                parts.Add($"CTA: {payload.CtaText}");
            }

            scriptBody = string.Join("\n\n", parts);
        }

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
                ["script"] = scriptBody
            },
            Dimensions: CompositionDefaults.StandardDimensions)
        {
            OutputKind = CompositionOutputKind.Markdown,
            TextBody = scriptBody,
        };
    }
}
