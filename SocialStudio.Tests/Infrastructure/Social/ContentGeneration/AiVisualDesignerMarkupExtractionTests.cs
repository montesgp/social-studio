using SocialStudio.Infrastructure.Social.ContentGeneration;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Regression: the visual designer must not render an LLM's chatty preamble / Markdown code fence
/// onto the slide. A weaker model returned prose + a ```html fence around the composition; the
/// guardrail validated the nested composition subtree but the RENDERER received the raw string
/// unchanged, so the explanation text ("Acá está la composición…") was printed onto the slide.
/// ExtractCompositionMarkup must strip the fence and surrounding prose before render.
/// </summary>
public sealed class AiVisualDesignerMarkupExtractionTests
{
    private const string Composition =
        "<div class=\"composition\">" +
        "<div data-layer=\"background\"></div>" +
        "<div data-layer=\"content\"><h1 class=\"slot\" data-slot=\"title\">Hola</h1></div>" +
        "</div>";

    [Fact]
    public void ExtractCompositionMarkup_PreambleAndCodeFence_ReturnsOnlyComposition()
    {
        var raw =
            "Acá está la composición HTML/CSS que ese prompt debería producir, siguiendo el " +
            "OUTPUT CONTRACT al pie: dos capas (background + content), solo tags permitidos.\n" +
            "```html\n" +
            Composition + "\n" +
            "```";

        var result = AiVisualDesigner.ExtractCompositionMarkup(raw);

        Assert.Equal(Composition, result);
        Assert.DoesNotContain("Acá está la composición", result);
        Assert.DoesNotContain("```", result);
    }

    [Fact]
    public void ExtractCompositionMarkup_LeadingProseNoFence_StripsProse()
    {
        var raw = "Here is the design you asked for: " + Composition;

        var result = AiVisualDesigner.ExtractCompositionMarkup(raw);

        Assert.Equal(Composition, result);
    }

    [Fact]
    public void ExtractCompositionMarkup_CleanMarkup_ReturnedUnchanged()
    {
        var result = AiVisualDesigner.ExtractCompositionMarkup(Composition);

        Assert.Equal(Composition, result);
    }

    [Fact]
    public void ExtractCompositionMarkup_ProseOnlyNoMarkup_ReturnsEmpty()
    {
        var raw = "Sorry, I can't produce that composition right now.";

        var result = AiVisualDesigner.ExtractCompositionMarkup(raw);

        // No markup at all → empty, so the caller degrades the slide instead of rendering prose.
        Assert.True(string.IsNullOrEmpty(result));
    }
}
