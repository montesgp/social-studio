using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class CarouselVisualComposer : IFormatVisualComposer, IMultiAssetVisualComposer
{
    public string FormatKey => "carousel";

    /// <summary>
    /// Legacy single-spec path: returns a spec for the first slide only.
    /// Retained for backward compatibility — the pipeline now uses <see cref="ComposeMany"/>.
    /// </summary>
    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var specs = ComposeMany(generatedPayload, brand, inferred);
        return specs.Count > 0
            ? specs[0]
            : BuildSlideSpec(brand, inferred, null, 1, "Carousel Title", string.Empty);
    }

    /// <summary>
    /// Returns one <see cref="VisualCompositionSpec"/> per slide in the carousel payload.
    /// The pipeline renders each spec independently, producing slide_1.png, slide_2.png, …
    /// </summary>
    public IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred)
    {
        var payload = PayloadReader.As<CarouselContent>(generatedPayload);
        if (payload is null || payload.Slides.Count == 0)
        {
            return [];
        }

        var specs = new List<VisualCompositionSpec>(payload.Slides.Count);
        for (var i = 0; i < payload.Slides.Count; i++)
        {
            var slide = payload.Slides[i];
            var slideIndex = i + 1; // 1-based
            specs.Add(BuildSlideSpec(brand, inferred, slide, slideIndex, payload.CoverTitle, payload.CtaText));
        }

        return specs;
    }

    private VisualCompositionSpec BuildSlideSpec(
        BrandSnapshot brand,
        InferenceAccumulator inferred,
        CarouselSlide? slide,
        int slideIndex,
        string coverTitle,
        string ctaText)
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
                [$"slide-{slideIndex}"] = new AssetSlotSpec(null, slide?.ImagePromptHint ?? $"Slide {slideIndex} visual"),
                ["logo"] = new AssetSlotSpec(brand.Brand?.Logos.FirstOrDefault(), "Brand logo"),
            },
            CopySlots: new Dictionary<string, string>
            {
                ["title"] = slideIndex == 1 ? coverTitle : (slide?.Title ?? $"Slide {slideIndex}"),
                ["subtitle"] = slide is null ? "Supporting copy" : slide.Body,
                ["cta"] = ctaText,
            },
            Dimensions: CompositionDefaults.StandardDimensions);
    }
}
