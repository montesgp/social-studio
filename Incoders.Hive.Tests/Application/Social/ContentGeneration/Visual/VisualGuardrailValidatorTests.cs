using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// B1a TDD tests for VisualGuardrailValidator — all 8 scenarios.
/// Spec: VD-02 (palette + typography), VD-03 (overflow), VD-04 (schema + PromptVersionKey).
/// Tasks: 4.4
/// </summary>
public sealed class VisualGuardrailValidatorTests
{
    private static readonly IVisualGuardrailValidator Validator = new VisualGuardrailValidator();

    private static BrandManualSnapshot BuildBrand(
        string[]? palette = null,
        string[]? typography = null) =>
        new(
            ColorPalette: palette ?? new[] { "#FFFFFF", "#000000", "#0055FF", "#333333", "#F9FAFB" },
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: typography ?? new[] { "Inter", "Roboto" },
            VoiceAndTone: null,
            Notes: null);

    private static VisualCompositionSpec BuildSpec(int width = 1080, int height = 1920) =>
        new(
            FormatKey: "flyer",
            LayoutType: "story-flyer",
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
            $"""<div class="composition" data-format="flyer" style="width:1080px;height:1920px;{extra}"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="{ffStyle}color:#FFFFFF;">{headline}</h1></div></div>""";
    }

    // -----------------------------------------------------------------------
    // VD-02 (advisory): Off-palette color does NOT block generation — render anyway.
    // Product decision: the brand palette is a strong preference, not a hard gate. A flyer must
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
    // VD-02 (advisory): Non-manual font (Arial) does NOT block generation — render anyway.
    // -----------------------------------------------------------------------

    [Fact]
    public void NonManualFont_Arial_IsAdvisory_RendersAnyway()
    {
        // GIVEN markup with font-family:Arial, manual does not list Arial
        var markup = ValidMarkup(fontFamily: "Arial");
        var output = ValidOutput(markup);
        var brand = BuildBrand(); // only "Inter", "Roboto" — no Arial
        var spec = BuildSpec();

        // WHEN guardrail validation runs
        var result = Validator.Validate(output, brand, spec);

        // THEN it is NOT blocked — the design is forwarded to the renderer.
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
            $"""<div class="composition" data-format="flyer" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="1" style="font-family:Inter;color:#FFFFFF;">{longText}</h1></div></div>""";
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
}
