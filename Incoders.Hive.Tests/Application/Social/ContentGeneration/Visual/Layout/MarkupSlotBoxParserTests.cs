using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Layout;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual.Layout;

/// <summary>
/// WU-4 TDD tests for <see cref="MarkupSlotBoxParser"/> — the reusable declared-box parser.
/// Spec: CLIF-CG-04 (design.md file map). Tasks: 4.8.
/// </summary>
public sealed class MarkupSlotBoxParserTests
{
    [Fact]
    public void ParseDeclaredSlotBoxes_ParsesLeftTopWidthMinHeight_PerDataSlot()
    {
        const string markup =
            """
            <div class="composition">
              <div data-layer="content">
                <h1 class="slot" data-slot="headline" data-max-lines="3" style="position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;">Headline</h1>
                <p class="slot" data-slot="subtitle" data-max-lines="6" style="position:absolute;left:100px;top:575px;width:825px;min-height:122px;color:#fff;">Subtitle</p>
              </div>
            </div>
            """;

        var boxes = MarkupSlotBoxParser.ParseDeclaredSlotBoxes(markup);

        Assert.Equal(2, boxes.Count);
        var headline = boxes.Single(b => b.Slot == "headline");
        Assert.Equal(96, headline.Left);
        Assert.Equal(255, headline.Top);
        Assert.Equal(888, headline.Width);
        Assert.Equal(260, headline.Height);

        var subtitle = boxes.Single(b => b.Slot == "subtitle");
        Assert.Equal(575, subtitle.Top);
        Assert.Equal(122, subtitle.Height);
    }

    [Fact]
    public void ParseDeclaredSlotBoxes_SkipsBottomAnchoredSlots()
    {
        // GIVEN a bottom-anchored cta/accent/signature/brand-mark slot mixed with top-anchored ones —
        // the reflowable-slot-set rule excludes bottom-anchored slots BY CONSTRUCTION.
        const string markup =
            """
            <div class="composition">
              <div data-layer="content">
                <h1 class="slot" data-slot="headline" data-max-lines="3" style="position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;">Headline</h1>
                <p class="slot" data-slot="cta" data-max-lines="1" style="position:absolute;left:100px;bottom:245px;width:640px;min-height:58px;color:#fff;">CTA</p>
                <p class="slot" data-slot="signature" data-max-lines="1" style="position:absolute;left:100px;bottom:120px;width:400px;min-height:40px;color:#fff;">Sig</p>
              </div>
            </div>
            """;

        var boxes = MarkupSlotBoxParser.ParseDeclaredSlotBoxes(markup);

        Assert.Single(boxes);
        Assert.Equal("headline", boxes[0].Slot);
    }

    [Fact]
    public void ParseDeclaredSlotBoxes_MissingRequiredDimension_SlotIsSkipped()
    {
        // No min-height declared for "orphan" — one of the four required dimensions is missing.
        const string markup =
            """
            <div class="composition">
              <div data-layer="content">
                <h1 class="slot" data-slot="headline" data-max-lines="3" style="position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;">Headline</h1>
                <p class="slot" data-slot="orphan" style="position:absolute;left:100px;top:600px;width:400px;color:#fff;">Orphan</p>
              </div>
            </div>
            """;

        var boxes = MarkupSlotBoxParser.ParseDeclaredSlotBoxes(markup);

        Assert.Single(boxes);
        Assert.Equal("headline", boxes[0].Slot);
    }
}
