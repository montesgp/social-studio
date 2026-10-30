using SocialStudio.Application.Features.Social.ContentGeneration.Formats.CtaVariation;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class CtaVariationVisualComposer : TextCardVisualComposerBase
{
    public override string FormatKey => "cta-variation";
    protected override string SlotKey => "text-card";

    protected override IReadOnlyDictionary<string, string> BuildCopySlots(object generatedPayload)
    {
        var payload = PayloadReader.As<CtaVariationContent>(generatedPayload);

        return new Dictionary<string, string>
        {
            ["text"] = payload is null
                ? "Generated text content"
                : string.Join(" | ", payload.Variations.Select(variation => $"{variation.Text} ({variation.Intent})"))
        };
    }
}
