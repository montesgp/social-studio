using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Domain.Social.ClientBrandManuals;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// B1a TDD tests for VisualGuardrailValidator — all 8 scenarios.
/// Spec: VD-02 (palette + typography), VD-03 (overflow), VD-04 (schema + PromptVersionKey).
/// Tasks: 4.4
/// </summary>
public sealed class VisualGuardrailValidatorTests
{
    private static readonly IVisualGuardrailValidator Validator = new VisualGuardrailValidator();

    private static BrandManualSnapshot BuildBrand(
        WeightedColor[]? palette = null,
        string[]? typography = null) =>
        new(
            ColorPalette: palette ?? new[]
            {
                new WeightedColor("#FFFFFF", null, 0),
                new WeightedColor("#000000", null, 1),
                new WeightedColor("#0055FF", null, 2),
                new WeightedColor("#333333", null, 3),
                new WeightedColor("#F9FAFB", null, 4),
            },
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: typography ?? new[] { "Inter", "Roboto" },
            VoiceAndTone: null,
            Notes: null);

    private static VisualCompositionSpec BuildSpec(int width = 1080, int height = 1920) =>
        new(
            FormatKey: "story",
            LayoutType: "story-story",
            Palette: new CompositionPalette("#FFFFFF", "#000000", "#0055FF", "#333333", "#F9FAFB"),
            Typography: new CompositionTypography("Inter", 32, "Roboto", 16),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Hello World" },
            Dimensions: new CompositionDimensions(width, height, 96));

    // Valid constrained HTML (for reuse across tests)
    private static VisualDesignOutput ValidOutput(string? markup = null, string? key = null) =>
        new(
            Markup: markup ?? ValidMarkup(),
            PromptVersionKey: key ?? "visual-designer/v1",
            IsValid: false);

    private static string ValidMarkup(
        string? extraStyle = null,
        string? headlineText = null,
        string? fontFamily = null)
    {
        var ffStyle = fontFamily is not null ? $"font-family:{fontFamily};" : "font-family:Inter;";
        var extra = extraStyle ?? string.Empty;
        var headline = headlineText ?? "Hello World";
        return
            $"""<div class="composition" data-format="story" style="width:1080px;height:1920px;{extra}"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="{ffStyle}color:#FFFFFF;">{headline}</h1></div></div>""";
    }

    // -----------------------------------------------------------------------
    // VD-02 (advisory): Off-palette color does NOT block generation — render anyway.
    // Product decision: the brand palette is a strong preference, not a hard gate. A story must
    // always generate even if the AI deviates (e.g. white text for legibility).
    // -----------------------------------------------------------------------

    [Fact]
    public void OffPaletteColor_IsAdvisory_RendersAnyway()
    {
        // GIVEN markup with off-palette hex (#FF00FF not in brand palette)
        var markup = ValidMarkup(extraStyle: "background-color:#FF00FF;");
        var output = ValidOutput(markup);
        var brand = BuildBrand();
        var spec = BuildSpec();

        // WHEN guardrail validation runs
        var result = Validator.Validate(output, brand, spec);

        // THEN it is NOT blocked — the design is forwarded to the renderer.
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsValid);
    }

    // -----------------------------------------------------------------------
    // R4.3 (HARD, waiver removed): a foreign named font not in the brand/catalog allow-list is
    // rejected outright — this is a hard rejection, not a silent pass (Decision D2).
    // -----------------------------------------------------------------------

    [Fact]
    public void ForeignNamedFont_NotInBrandOrCatalog_IsHardRejected()
    {
        // GIVEN markup with font-family:Comic Sans MS, manual does not list it and it is not a
        // catalog system-safe-list font either.
        var markup = ValidMarkup(fontFamily: "Comic Sans MS");
        var output = ValidOutput(markup);
        var brand = BuildBrand(); // only "Inter", "Roboto"
        var spec = BuildSpec();

        // WHEN guardrail validation runs
        var result = Validator.Validate(output, brand, spec);

        // THEN generation fails validation for this slide — a hard rejection, not a silent pass.
        Assert.True(result.IsFailure);
    }

    // -----------------------------------------------------------------------
    // R4.3 (HARD): a CSS-generic-only declaration that reaches the validator (i.e. normalization was
    // skipped upstream) is now rejected too — the old unconditional waiver is gone. This is the
    // guardrail's own loud failure for a pipeline bug, per Decision D2.
    // -----------------------------------------------------------------------

    [Fact]
    public void GenericOnlyFontFamily_WithoutPriorNormalization_IsHardRejected()
    {
        var markup = ValidMarkup(fontFamily: "sans-serif");
        var output = ValidOutput(markup);
        var brand = BuildBrand();
        var spec = BuildSpec();

        var result = Validator.Validate(output, brand, spec);

        Assert.True(result.IsFailure);
    }

    // -----------------------------------------------------------------------
    // F2: the allow-list is sourced from FontCatalog.For(brand), not brand.Typography directly — so
    // a system-safe-list font (e.g. one chosen via the picker) is accepted even though it is not in
    // the brand manual.
    // -----------------------------------------------------------------------

    [Fact]
    public void CatalogSystemSafeFont_NotInBrandManual_IsAccepted()
    {
        var markup = ValidMarkup(fontFamily: "Liberation Sans");
        var output = ValidOutput(markup);
        var brand = BuildBrand(); // only "Inter", "Roboto" — Liberation Sans comes from the catalog
        var spec = BuildSpec();

        var result = Validator.Validate(output, brand, spec);

        Assert.True(result.IsSuccess);
    }

    // -----------------------------------------------------------------------
    // VD-02: On-palette and on-font output passes guardrails
    // -----------------------------------------------------------------------

    [Fact]
    public void OnPaletteAndFont_Passes()
    {
        // GIVEN markup using brand colors and brand fonts only
        var markup = ValidMarkup(); // uses #FFFFFF, #000000, Inter — all in brand
        var output = ValidOutput(markup);
        var brand = BuildBrand();
        var spec = BuildSpec();

        // WHEN guardrail validation runs
        var result = Validator.Validate(output, brand, spec);

        // THEN result is successful and forwarded to renderer
        Assert.True(result.IsSuccess);
    }

    // -----------------------------------------------------------------------
    // VD-03: Text within bounds passes
    // -----------------------------------------------------------------------

    [Fact]
    public void TextWithinBounds_Passes()
    {
        // GIVEN all text content fits within declared data-max-lines
        // "Hello World" is 11 chars, data-max-lines=3 => well within estimate
        var markup = ValidMarkup(headlineText: "Hello World");
        var output = ValidOutput(markup);
        var brand = BuildBrand();
        var spec = BuildSpec();

        // WHEN overflow validation runs
        var result = Validator.Validate(output, brand, spec);

        // THEN result is successful
        Assert.True(result.IsSuccess);
    }

    // -----------------------------------------------------------------------
    // VD-03: Text overflow rejected before render
    // -----------------------------------------------------------------------

    [Fact]
    public void TextOverflow_Rejected_BeforeRender()
    {
        // GIVEN a text block with extremely long content that exceeds data-max-lines=1
        // data-max-lines=1 but we pass a 300-character string — static estimate must fail
        var longText = new string('A', 300);
        // Build markup with data-max-lines=1 to force overflow
        var markup =
            $"""<div class="composition" data-format="story" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="1" style="font-family:Inter;color:#FFFFFF;">{longText}</h1></div></div>""";
        var output = ValidOutput(markup);
        var brand = BuildBrand();
        var spec = BuildSpec();

        // WHEN overflow validation runs
        var result = Validator.Validate(output, brand, spec);

        // THEN failed Result with TextOverflow error
        Assert.True(result.IsFailure);
        Assert.Equal("visual-guardrail.text-overflow", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // VD-04: Invalid markup rejected before render
    // -----------------------------------------------------------------------

    [Fact]
    public void InvalidMarkup_Rejected()
    {
        // GIVEN markup missing div.composition root (plain text)
        var output = ValidOutput(markup: "<div class=\"wrong\">not a composition</div>");
        var brand = BuildBrand();
        var spec = BuildSpec();

        // WHEN schema validation runs
        var result = Validator.Validate(output, brand, spec);

        // THEN failed Result with schema error
        Assert.True(result.IsFailure);
        Assert.Equal("visual-guardrail.invalid-markup", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // VD-04: Valid markup with PromptVersionKey passes
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidMarkup_PreservesPromptVersionKey()
    {
        // GIVEN VisualDesignOutput has valid markup and non-empty PromptVersionKey
        const string key = "visual-designer/v1";
        var output = ValidOutput(key: key);
        var brand = BuildBrand();
        var spec = BuildSpec();

        // WHEN schema validation runs
        var result = Validator.Validate(output, brand, spec);

        // THEN result successful; PromptVersionKey preserved in forwarded output
        Assert.True(result.IsSuccess);
        Assert.Equal(key, result.Value.PromptVersionKey);
    }

    // -----------------------------------------------------------------------
    // VD-04: Stub designer — no real LLM, key non-empty
    // -----------------------------------------------------------------------

    [Fact]
    public void StubDesigner_NoRealLlm_KeyNonEmpty()
    {
        // GIVEN test-double IVisualDesigner returns a fixed stub output
        var output = ValidOutput(key: "visual-designer/v1");
        var brand = BuildBrand();
        var spec = BuildSpec();

        // WHEN all validation runs (no real LLM call)
        var result = Validator.Validate(output, brand, spec);

        // THEN all validation steps complete, PromptVersionKey is non-empty
        // (may succeed or fail on content, but must NOT throw and key must be non-empty)
        if (result.IsSuccess)
        {
            Assert.NotEmpty(result.Value.PromptVersionKey);
        }
        else
        {
            // If guardrail fails, it must not be because PromptVersionKey was empty
            Assert.NotEqual("visual-guardrail.invalid-markup", result.Error.Code);
        }

        // Direct check: ValidOutput produces valid markup — must succeed
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.PromptVersionKey);
    }

    // -----------------------------------------------------------------------
    // VD-04 (robustness): the designer emits HTML, not XML. Render-valid HTML-isms that the
    // strict XmlDocument parser used to reject must NOT block generation. Regression tests for the
    // intermittent `invalid-markup` story failures (Chromium renders all of these fine).
    // -----------------------------------------------------------------------

    [Fact]
    public void UnclosedVoidImg_InBackground_Passes()
    {
        // GIVEN a background <img> in HTML form (NOT self-closed) — valid HTML, invalid raw XML.
        var markup =
            """<div class="composition" style="width:1080px;height:1920px;"><div data-layer="background"><img src="https://cdn.example/bg.png" style="width:1080px;height:1920px;"></div><div data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Hola</h1></div></div>""";

        var result = Validator.Validate(ValidOutput(markup), BuildBrand(), BuildSpec());

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
    }

    [Fact]
    public void BrAndEmphasisTags_InContent_Pass()
    {
        // GIVEN line breaks (<br>) and inline emphasis (<strong>) — common in multi-line body copy.
        var markup =
            """<div class="composition" style="width:1080px;height:1920px;"><div data-layer="background" style="background:#000000;"></div><div data-layer="content"><p class="slot" data-slot="body" data-max-lines="4" style="font-family:Inter;color:#FFFFFF;">Linea uno<br>Linea <strong>dos</strong></p></div></div>""";

        var result = Validator.Validate(ValidOutput(markup), BuildBrand(), BuildSpec());

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
    }

    [Fact]
    public void HtmlNamedEntities_Pass()
    {
        // GIVEN HTML named entities XML does not define (&nbsp; &mdash; &eacute;).
        var markup =
            """<div class="composition" style="width:1080px;height:1920px;"><div data-layer="background" style="background:#000000;"></div><div data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Caf&eacute;&nbsp;&mdash;&nbsp;resumen</h1></div></div>""";

        var result = Validator.Validate(ValidOutput(markup), BuildBrand(), BuildSpec());

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
    }

    [Fact]
    public void BareAmpersandInText_Passes()
    {
        // GIVEN a raw ampersand in text content (e.g. a brand name) — invalid raw XML.
        var markup =
            """<div class="composition" style="width:1080px;height:1920px;"><div data-layer="background" style="background:#000000;"></div><div data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Tom & Jerry &amp; Co</h1></div></div>""";

        var result = Validator.Validate(ValidOutput(markup), BuildBrand(), BuildSpec());

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
    }

    [Fact]
    public void RawLessThanInsideAttributeValue_Passes()
    {
        // GIVEN render-safe HTML with a CSS decorative character in an inline style.
        // XmlDocument rejects raw '<' inside attributes, but Chromium renders it; this exact
        // parser strictness was blocking story generation after the visual designer succeeded.
        var markup =
            """<div class="composition" style="width:1080px;height:1920px;"><div data-layer="background" style="background:#000000;"></div><div data-layer="content"><span class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;content:'<';">Hola</span></div></div>""";

        var result = Validator.Validate(ValidOutput(markup), BuildBrand(), BuildSpec());

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
    }

    [Fact]
    public void FullHtmlDocument_WithComposition_Passes()
    {
        // GIVEN a weaker model wraps the composition in a FULL HTML document — <!DOCTYPE>, <html>,
        // <head> and a class-based <style> block — exactly the production failure that hard-rejected
        // the story even though Chromium renders this markup perfectly. The guardrail must validate
        // the composition subtree and ignore the document wrapper.
        var markup =
            """
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="UTF-8" />
            <style>
              * { margin: 0; padding: 0; box-sizing: border-box; }
              .composition { width: 1080px; height: 1920px; position: relative; }
            </style>
            </head>
            <body>
            <div class="composition" data-format="story" style="width:1080px;height:1920px;">
              <div data-layer="background" style="background:#000000;"></div>
              <div data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Hola</h1></div>
            </div>
            </body>
            </html>
            """;

        var result = Validator.Validate(ValidOutput(markup), BuildBrand(), BuildSpec());

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
    }

    [Fact]
    public void FullHtmlDocument_WithoutComposition_StillRejected()
    {
        // GIVEN a full HTML document that does NOT contain a .composition element. Tolerating the
        // wrapper must NOT let genuinely structureless markup through.
        var markup =
            """
            <!DOCTYPE html>
            <html>
            <head><style>.x { color: red; }</style></head>
            <body><div class="wrapper"><p>no composition here</p></div></body>
            </html>
            """;

        var result = Validator.Validate(ValidOutput(markup), BuildBrand(), BuildSpec());

        Assert.True(result.IsFailure);
        Assert.Equal("visual-guardrail.invalid-markup", result.Error.Code);
    }

    [Fact]
    public void DisallowedTag_StillRejected()
    {
        // GIVEN a structurally well-formed composition that contains a tag outside the allow-list.
        // The relaxed normalization must NOT let genuinely off-schema markup through.
        var markup =
            """<div class="composition" style="width:1080px;height:1920px;"><div data-layer="background" style="background:#000000;"></div><div data-layer="content"><script>doEvil()</script></div></div>""";

        var result = Validator.Validate(ValidOutput(markup), BuildBrand(), BuildSpec());

        Assert.True(result.IsFailure);
        Assert.Equal("visual-guardrail.invalid-markup", result.Error.Code);
    }
}
