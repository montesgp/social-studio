using SocialStudio.Application.Features.Social.ContentGeneration.Formats.ContentIdea;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class ContentIdeaVisualComposer : IFormatVisualComposer
{
    public string FormatKey => "content-idea";

    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var payload = PayloadReader.As<ContentIdeaContent>(generatedPayload);
        var palette = CompositionDefaults.ResolvePalette(brand, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);

        var ideaBody = payload is null
            ? "Content idea summary"
            : $"{payload.IdeaTitle}: {payload.Angle}. {payload.Rationale}";

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
                ["idea"] = ideaBody
            },
            Dimensions: CompositionDefaults.StandardDimensions)
        {
            OutputKind = CompositionOutputKind.Text,
            TextBody = ideaBody,
        };
    }
}
