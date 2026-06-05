using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// TDD tests for <see cref="VisualCompositionSpec"/> invariants and equality.
/// RED phase: references types that don't exist yet.
/// </summary>
public sealed class VisualCompositionSpecTests
{
    [Fact]
    public void Create_WithValidArguments_Succeeds()
    {
        var spec = BuildValid();

        Assert.Equal("carousel", spec.FormatKey);
        Assert.Equal("grid", spec.LayoutType);
        Assert.NotNull(spec.Palette);
        Assert.NotNull(spec.Typography);
        Assert.NotNull(spec.AssetSlots);
        Assert.NotNull(spec.CopySlots);
        Assert.NotNull(spec.Dimensions);
    }

    [Fact]
    public void Palette_WithValidHexColors_IsAccepted()
    {
        var palette = new CompositionPalette(
            Primary: "#FF5733",
            Secondary: "#C70039",
            Accent: "#FFC300",
            Text: "#000000",
            Background: "#FFFFFF");

        Assert.Equal("#FF5733", palette.Primary);
        Assert.Equal("#FFFFFF", palette.Background);
    }

    [Fact]
    public void AssetSlots_HaveUniqueKeys_NoConflict()
    {
        var slots = new Dictionary<string, AssetSlotSpec>
        {
            ["cover"] = new AssetSlotSpec("https://example.com/img.png", null),
            ["slide-1"] = new AssetSlotSpec(null, "Brand product photo")
        };

        var spec = BuildValid(assetSlots: slots);

        Assert.Equal(2, spec.AssetSlots.Count);
        Assert.True(spec.AssetSlots.ContainsKey("cover"));
        Assert.True(spec.AssetSlots.ContainsKey("slide-1"));
    }

    [Fact]
    public void CopySlots_HaveUniqueKeys_NoConflict()
    {
        var copySlots = new Dictionary<string, string>
        {
            ["title"] = "Main heading text",
            ["subtitle"] = "Supporting copy"
        };

        var spec = BuildValid(copySlots: copySlots);

        Assert.Equal(2, spec.CopySlots.Count);
        Assert.Equal("Main heading text", spec.CopySlots["title"]);
    }

    [Fact]
    public void Equality_TwoSpecsWithSameFormatKey_HaveSameFormatKey()
    {
        // Records with IReadOnlyDictionary properties use reference equality for collections.
        // We verify the scalar fields that ARE structurally comparable.
        var spec1 = BuildValid("carousel");
        var spec2 = BuildValid("carousel");

        Assert.Equal(spec1.FormatKey, spec2.FormatKey);
        Assert.Equal(spec1.LayoutType, spec2.LayoutType);
        Assert.Equal(spec1.Palette, spec2.Palette);
        Assert.Equal(spec1.Typography, spec2.Typography);
        Assert.Equal(spec1.Dimensions, spec2.Dimensions);
    }

    [Fact]
    public void Equality_TwoSpecsWithDifferentFormatKey_FormatKeysDiffer()
    {
        var spec1 = BuildValid("carousel");
        var spec2 = BuildValid("flyer");

        Assert.NotEqual(spec1.FormatKey, spec2.FormatKey);
    }

    [Fact]
    public void Dimensions_WidthHeightDpi_ArePreserved()
    {
        var dimensions = new CompositionDimensions(Width: 1080, Height: 1080, Dpi: 96);

        var spec = BuildValid(dimensions: dimensions);

        Assert.Equal(1080, spec.Dimensions.Width);
        Assert.Equal(1080, spec.Dimensions.Height);
        Assert.Equal(96, spec.Dimensions.Dpi);
    }

    // -------------------------------------------------------------------
    // T1.1 — CompositionOutputKind enum + OutputKind/TextBody on spec
    // -------------------------------------------------------------------

    [Fact]
    public void OutputKind_Default_IsRaster()
    {
        var spec = BuildValid();

        Assert.Equal(CompositionOutputKind.Raster, spec.OutputKind);
    }

    [Fact]
    public void OutputKind_WhenSetToText_IsPreserved()
    {
        var spec = BuildValid() with { OutputKind = CompositionOutputKind.Text };

        Assert.Equal(CompositionOutputKind.Text, spec.OutputKind);
    }

    [Fact]
    public void TextBody_Default_IsNull()
    {
        var spec = BuildValid();

        Assert.Null(spec.TextBody);
    }

    [Fact]
    public void TextBody_WhenSet_IsPreserved()
    {
        var spec = BuildValid() with { TextBody = "Some reel script text" };

        Assert.Equal("Some reel script text", spec.TextBody);
    }

    [Fact]
    public void OutputKindAndTextBody_SetTogether_ArePreserved()
    {
        var spec = BuildValid() with
        {
            OutputKind = CompositionOutputKind.Text,
            TextBody = "Hook text body"
        };

        Assert.Equal(CompositionOutputKind.Text, spec.OutputKind);
        Assert.Equal("Hook text body", spec.TextBody);
    }

    // -------------------------------------------------------------------
    // A-1 [RS-01] — CompositionOutputKind enum has Markdown member
    // -------------------------------------------------------------------

    [Fact]
    public void CompositionOutputKind_HasMarkdownMember()
    {
        // RS-01 scenario: "Markdown member exists"
        Assert.True(
            Enum.IsDefined(typeof(CompositionOutputKind), "Markdown"),
            "CompositionOutputKind must define a 'Markdown' member (RS-01).");
    }

    [Fact]
    public void OutputKind_WhenSetToMarkdown_IsPreserved()
    {
        var spec = BuildValid() with { OutputKind = CompositionOutputKind.Markdown };

        Assert.Equal(CompositionOutputKind.Markdown, spec.OutputKind);
    }

    [Fact]
    public void ExistingSpecConstruction_WithoutOutputKind_DefaultsToRaster_BackwardCompat()
    {
        // Verifies backward compat: constructing without OutputKind still defaults to Raster.
        var spec = new VisualCompositionSpec(
            FormatKey: "carousel",
            LayoutType: "grid",
            Palette: new CompositionPalette("#000", "#333", "#666", "#FFF", "#EEE"),
            Typography: new CompositionTypography("Roboto", 24, "Roboto", 16),
            AssetSlots: new Dictionary<string, AssetSlotSpec> { ["cover"] = new AssetSlotSpec(null, null) },
            CopySlots: new Dictionary<string, string> { ["title"] = "Title" },
            Dimensions: new CompositionDimensions(1080, 1080, 96));

        Assert.Equal(CompositionOutputKind.Raster, spec.OutputKind);
        Assert.Null(spec.TextBody);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static VisualCompositionSpec BuildValid(
        string formatKey = "carousel",
        IReadOnlyDictionary<string, AssetSlotSpec>? assetSlots = null,
        IReadOnlyDictionary<string, string>? copySlots = null,
        CompositionDimensions? dimensions = null)
    {
        return new VisualCompositionSpec(
            FormatKey: formatKey,
            LayoutType: "grid",
            Palette: new CompositionPalette("#000", "#333", "#666", "#FFF", "#EEE"),
            Typography: new CompositionTypography("Roboto", 24, "Roboto", 16),
            AssetSlots: assetSlots ?? new Dictionary<string, AssetSlotSpec>
            {
                ["cover"] = new AssetSlotSpec(null, "Brand image")
            },
            CopySlots: copySlots ?? new Dictionary<string, string>
            {
                ["title"] = "Hello World"
            },
            Dimensions: dimensions ?? new CompositionDimensions(1080, 1080, 96));
    }
}
