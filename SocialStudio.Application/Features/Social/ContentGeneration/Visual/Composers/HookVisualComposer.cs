using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Hook;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class HookVisualComposer : TextCardVisualComposerBase
{
    public override string FormatKey => "hook";
    protected override string SlotKey => "text-card";

    protected override IReadOnlyDictionary<string, string> BuildCopySlots(object generatedPayload)
    {
        var payload = PayloadReader.As<HookContent>(generatedPayload);

        return new Dictionary<string, string>
        {
            ["text"] = payload is null
                ? "Generated text content"
                : $"{payload.PrimaryHook} Tone: {payload.Tone}. Pattern: {payload.Pattern}."
        };
    }
}
