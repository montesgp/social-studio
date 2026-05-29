namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class HookVisualComposer : TextCardVisualComposerBase
{
    public override string FormatKey => "hook";
    protected override string SlotKey => "text-card";
}
