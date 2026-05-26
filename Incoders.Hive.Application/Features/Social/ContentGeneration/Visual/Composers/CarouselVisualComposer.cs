using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class CarouselVisualComposer : IFormatVisualComposer
{
    public string FormatKey => "carousel";

    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var palette = CompositionDefaults.ResolvePalette(brand, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: "grid",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["cover"] = new AssetSlotSpec(null, "Brand cover image — carousel"),
                ["slide-1"] = new AssetSlotSpec(null, "Slide 1 visual"),
            },
            CopySlots: new Dictionary<string, string>
            {
                ["title"] = "Carousel Title",
                ["subtitle"] = "Supporting copy"
            },
            Dimensions: CompositionDefaults.StandardDimensions);
    }
}
