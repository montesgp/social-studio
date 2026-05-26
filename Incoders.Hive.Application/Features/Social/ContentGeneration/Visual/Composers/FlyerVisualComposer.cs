using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class FlyerVisualComposer : IFormatVisualComposer
{
    public string FormatKey => "flyer";

    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var palette = CompositionDefaults.ResolvePalette(brand, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: "flyer-default",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["hero"] = new AssetSlotSpec(null, "Hero image for flyer"),
                ["logo"] = new AssetSlotSpec(
                    brand.Brand?.Logos.FirstOrDefault(),
                    "Brand logo placeholder"),
            },
            CopySlots: new Dictionary<string, string>
            {
                ["headline"] = "Flyer Headline",
                ["body"] = "Body copy",
                ["cta"] = "Call to Action"
            },
            Dimensions: new CompositionDimensions(1080, 1350, 96));
    }
}
