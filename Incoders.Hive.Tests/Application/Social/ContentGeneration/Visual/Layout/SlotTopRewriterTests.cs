using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Layout;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual.Layout;

/// <summary>
/// WU-4 TDD tests for <see cref="SlotTopRewriter"/> — pure string rewrite of the <c>top:NNNpx</c>
/// declaration inside an already-built markup string's per-slot <c>style</c> attribute.
/// Spec: CLIF-CG-04 (design.md D-C). Tasks: 4.7.
/// </summary>
public sealed class SlotTopRewriterTests
{
    private const string Markup =
        """
        <div class="composition">
          <div data-layer="content">
            <h1 class="slot" data-slot="headline" data-max-lines="3" style="position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;">Headline</h1>
            <p class="slot" data-slot="subtitle" data-max-lines="6" style="position:absolute;left:100px;top:575px;width:825px;min-height:122px;color:#fff;">Subtitle</p>
            <p class="slot" data-slot="cta" data-max-lines="1" style="position:absolute;left:100px;bottom:245px;width:640px;min-height:58px;color:#fff;">CTA</p>
          </div>
        </div>
        """;

    [Fact]
    public void Apply_RewritesTopDeclaration_ScopedToNamedSlotOnly()
    {
        var tops = new Dictionary<string, int> { ["subtitle"] = 762 };

        var rewritten = SlotTopRewriter.Apply(Markup, tops);

        Assert.Contains("data-slot=\"subtitle\" data-max-lines=\"6\" style=\"position:absolute;left:100px;top:762px;width:825px;min-height:122px;color:#fff;\"", rewritten);
        // Untouched slots — headline's own top and the bottom-anchored cta — stay byte-identical.
        Assert.Contains("data-slot=\"headline\" data-max-lines=\"3\" style=\"position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;\"", rewritten);
        Assert.Contains("bottom:245px", rewritten);
    }

    [Fact]
    public void Apply_EmptyTopsMap_ReturnsMarkupUnchanged()
    {
        var rewritten = SlotTopRewriter.Apply(Markup, new Dictionary<string, int>());

        Assert.Equal(Markup, rewritten);
    }

    [Fact]
    public void Apply_BottomAnchoredSlot_HasNoTopDeclaration_LeftByteIdentical()
    {
        // A tops map entry for a bottom-anchored slot (no "top:" declaration in its style) must be a
        // silent no-op for that slot — there is nothing to rewrite.
        var tops = new Dictionary<string, int> { ["cta"] = 999 };

        var rewritten = SlotTopRewriter.Apply(Markup, tops);

        Assert.Equal(Markup, rewritten);
    }

    [Fact]
    public void Apply_IsIdempotent_ApplyingSameMapTwiceProducesSameOutput()
    {
        var tops = new Dictionary<string, int> { ["subtitle"] = 762, ["headline"] = 255 };

        var once = SlotTopRewriter.Apply(Markup, tops);
        var twice = SlotTopRewriter.Apply(once, tops);

        Assert.Equal(once, twice);
    }
}
