using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class CarouselVisualComposer : IFormatVisualComposer
{
    public string FormatKey => "carousel";

    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var payload = PayloadReader.As<CarouselContent>(generatedPayload);
        var palette = CompositionDefaults.ResolvePalette(brand, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);
        var firstSlide = payload?.Slides.FirstOrDefault();

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: "grid",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["cover"] = new AssetSlotSpec(null, "Brand cover image - carousel"),
                ["slide-1"] = new AssetSlotSpec(null, firstSlide?.ImagePromptHint ?? "Slide 1 visual"),
                ["logo"] = new AssetSlotSpec(brand.Brand?.Logos.FirstOrDefault(), "Brand logo"),
            },
            CopySlots: new Dictionary<string, string>
            {
                ["title"] = payload?.CoverTitle ?? "Carousel Title",
                ["subtitle"] = firstSlide is null
                    ? "Supporting copy"
                    : $"{firstSlide.Title}: {firstSlide.Body}",
                ["cta"] = payload?.CtaText ?? string.Empty
            },
            Dimensions: CompositionDefaults.StandardDimensions);
    }
}
