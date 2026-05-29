using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class ContentIdeaVisualComposer : IFormatVisualComposer
{
    public string FormatKey => "content-idea";

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
                ["summary-card"] = new AssetSlotSpec(null, null)
            },
            CopySlots: new Dictionary<string, string>
            {
                ["idea"] = "Content idea summary"
            },
            Dimensions: CompositionDefaults.StandardDimensions);
    }
}
