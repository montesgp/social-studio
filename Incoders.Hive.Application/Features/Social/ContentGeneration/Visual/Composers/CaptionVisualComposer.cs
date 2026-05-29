namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class CaptionVisualComposer : TextCardVisualComposerBase
{
    public override string FormatKey => "caption";
    protected override string SlotKey => "text-card";
}
