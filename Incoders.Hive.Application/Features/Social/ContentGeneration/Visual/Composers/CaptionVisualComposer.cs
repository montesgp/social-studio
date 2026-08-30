using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Caption;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class CaptionVisualComposer : TextCardVisualComposerBase
{
    public override string FormatKey => "caption";
    protected override string SlotKey => "text-card";

    protected override IReadOnlyDictionary<string, string> BuildCopySlots(object generatedPayload)
    {
        var payload = PayloadReader.As<CaptionContent>(generatedPayload);
        var suffix = payload is null
            ? string.Empty
            : string.Join(" ", payload.Hashtags.Concat(payload.Mentions ?? []));

        return new Dictionary<string, string>
        {
            ["text"] = payload is null
                ? "Generated text content"
                : $"{payload.PrimaryCaption} {suffix}".Trim()
        };
    }
}
