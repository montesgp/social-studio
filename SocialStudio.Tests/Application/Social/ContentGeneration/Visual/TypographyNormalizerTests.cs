using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// TDD tests for <see cref="TypographyNormalizer"/> — Decision D1 (Content Studio editing
/// completeness design). Normalizes AI-designed markup BEFORE guardrail validation runs, so cosmetic
/// omission (a CSS generic) does not become a hard generation failure, while an outright foreign
/// named font is left untouched for the guardrail to reject (Decision D1 rule 3).
/// </summary>
public sealed class TypographyNormalizerTests
{
    private static readonly CompositionTypography Effective =
        new(HeadingFamily: "Poppins", HeadingSize: 28, BodyFamily: "Poppins", BodySize: 16);

    private static readonly IReadOnlyList<FontEntry> Catalog = FontCatalog.For(new[] { "Poppins" });

    [Fact]
    public void Normalize_BrandFontAlreadyPresent_LeavesMarkupUntouched()
    {
        var markup = """<h1 style="font-family:Poppins;color:#FFF;">Hello</h1>""";

        var (result, report) = TypographyNormalizer.Normalize(markup, Effective, Catalog);

        Assert.Equal(markup, result);
        Assert.Equal(0, report.RewrittenCount);
    }

    [Fact]
    public void Normalize_GenericOnlyStack_IsRewrittenToBrandFontWithGenericTail()
    {
        var markup = """<h1 style="font-family:sans-serif;color:#FFF;">Hello</h1>""";

        var (result, report) = TypographyNormalizer.Normalize(markup, Effective, Catalog);

        // Single-quoted, deliberately: this is embedded inside an already-double-quoted HTML
        // style="..." attribute, so a double-quoted CSS string here would break the attribute.
        Assert.Contains("""font-family:'Poppins', sans-serif;""", result);
        Assert.Equal(1, report.RewrittenCount);
    }

    [Theory]
    [InlineData("serif")]
    [InlineData("system-ui")]
    [InlineData("monospace")]
    public void Normalize_OtherGenericKeywords_AreAlsoRewritten(string generic)
    {
        var markup = $"""<p style="font-family:{generic};">Hello</p>""";

        var (result, _) = TypographyNormalizer.Normalize(markup, Effective, Catalog);

        Assert.Contains($"""font-family:'Poppins', {generic};""", result);
    }

    [Fact]
    public void Normalize_ForeignNamedFont_IsPassedThroughUnchanged()
    {
        // Rule 3 (load-bearing): a foreign named font is NEVER silently rewritten — that would mask
        // prompt non-compliance. It is left for the guardrail to reject. Multi-word family, unquoted
        // in the fixture — the guardrail's matcher (and this normalizer's) does not require CSS
        // quoting around font names, matching how the rest of this codebase's fixtures build markup.
        var markup = """<h1 style="font-family:Comic Sans MS;color:#FFF;">Hello</h1>""";

        var (result, report) = TypographyNormalizer.Normalize(markup, Effective, Catalog);

        Assert.Equal(markup, result);
        Assert.Equal(0, report.RewrittenCount);
    }

    [Fact]
    public void Normalize_SystemCatalogFont_IsLeftUntouched()
    {
        // A system-safe-list font (e.g. operator-picked "Liberation Sans" persisted on a slot) is a
        // legitimate allowed value, not a generic and not foreign — must not be rewritten.
        var markup = """<h1 style="font-family:Liberation Sans;color:#FFF;">Hello</h1>""";

        var (result, report) = TypographyNormalizer.Normalize(markup, Effective, Catalog);

        Assert.Equal(markup, result);
        Assert.Equal(0, report.RewrittenCount);
    }

    [Fact]
    public void Normalize_MultipleDeclarations_RewritesOnlyGenericOnes()
    {
        var markup = """
            <div>
              <h1 style="font-family:sans-serif;">A</h1>
              <p style="font-family:Poppins;">B</p>
              <span style="font-family:Comic Sans MS;">C</span>
            </div>
            """;

        var (result, report) = TypographyNormalizer.Normalize(markup, Effective, Catalog);

        Assert.Contains("""font-family:'Poppins', sans-serif;""", result);
        Assert.Contains("""font-family:Poppins;""", result);
        Assert.Contains("""font-family:Comic Sans MS;""", result);
        Assert.Equal(1, report.RewrittenCount);
    }

    [Fact]
    public void Normalize_NoFontFamilyDeclarations_ReturnsMarkupUnchanged()
    {
        var markup = "<div>No fonts here</div>";

        var (result, report) = TypographyNormalizer.Normalize(markup, Effective, Catalog);

        Assert.Equal(markup, result);
        Assert.Equal(0, report.RewrittenCount);
    }
}
