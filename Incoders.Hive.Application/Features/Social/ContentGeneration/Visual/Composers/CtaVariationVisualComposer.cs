namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class CtaVariationVisualComposer : TextCardVisualComposerBase
{
    public override string FormatKey => "cta-variation";
    protected override string SlotKey => "text-card";
}
