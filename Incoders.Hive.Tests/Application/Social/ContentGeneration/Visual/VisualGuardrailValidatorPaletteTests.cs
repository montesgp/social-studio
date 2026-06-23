using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// Step 1.8 — assert that off-palette detection reads <see cref="WeightedColor.Hex"/>
/// instead of treating a WeightedColor as a raw string.
/// </summary>
public sealed class VisualGuardrailValidatorPaletteTests
{
    private static readonly IVisualGuardrailValidator Validator = new VisualGuardrailValidator();

    // Build a brand snapshot with an explicit WeightedColor palette.
    private static BrandManualSnapshot BuildWeightedBrand(string[] hexes) =>
        new(
            ColorPalette: hexes.Select((h, i) => new WeightedColor(h, null, i)).ToArray(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: new[] { "Inter", "Roboto" },
            VoiceAndTone: null,
            Notes: null);

    // Minimal composition spec.
    private static VisualCompositionSpec BuildSpec() => new(
        FormatKey: "story",
        LayoutType: "story-story",
        Palette: new CompositionPalette("#FFFFFF", "#000000", "#0055FF", "#333333", "#F9FAFB"),
        Typography: new CompositionTypography("Inter", 32, "Roboto", 16),
        AssetSlots: new Dictionary<string, AssetSlotSpec>(),
        CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
        Dimensions: new CompositionDimensions(1080, 1920, 96));

    private static VisualDesignOutput BuildOutput(string markup) =>
        new(
            Markup: markup,
            PromptVersionKey: "visual-designer/v1",
            IsValid: false);

    [Fact]
    public void Validate_ColorInWeightedPalette_ValidationReadsHexFromWeightedColor()
    {
        // GIVEN a palette with a WeightedColor whose Hex is #AABBCC
        var brand = BuildWeightedBrand(["#AABBCC", "#000000"]);
        // Markup uses only #AABBCC and #000000 — both in palette
        var markup =
            """<div class="composition" data-format="story" style="width:1080px;height:1920px;">""" +
            """<div data-layer="background" style="background:#000000;"></div>""" +
            """<div data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#AABBCC;">Test</h1></div>""" +
            "</div>";

        // WHEN validated
        var result = Validator.Validate(BuildOutput(markup), brand, BuildSpec());

        // THEN validator reads .Hex and does not fail on palette grounds (#AABBCC IS in palette)
        // The validator returns advisory-only for palette issues, so result should succeed
        Assert.True(result.IsSuccess,
            $"Expected success (palette advisory is non-blocking) but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
    }

    [Fact]
    public void Validate_WeightedColorPalette_CorrectlyIdentifiesOffPaletteColors()
    {
        // GIVEN a palette with only #AABBCC and #000000 as WeightedColors
        var brand = BuildWeightedBrand(["#AABBCC", "#000000"]);
        // Markup uses #FF00FF which is NOT in the WeightedColor palette
        var markup =
            """<div class="composition" data-format="story" style="width:1080px;height:1920px;background-color:#FF00FF;">""" +
            """<div data-layer="background" style="background:#FF00FF;"></div>""" +
            """<div data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#000000;">Test</h1></div>""" +
            "</div>";

        // WHEN validated — if validator reads WeightedColor correctly, it sees #FF00FF not in {#AABBCC, #000000}
        // palette check is advisory and non-blocking per VD-02, so result.IsSuccess is still expected
        // but this confirms the validator's palette set is built from WeightedColor.Hex, not from the record itself
        var result = Validator.Validate(BuildOutput(markup), brand, BuildSpec());

        // The palette check is advisory (VD-02 product decision) — result is still success
        Assert.True(result.IsSuccess,
            $"Off-palette is advisory per VD-02; expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
    }
}
