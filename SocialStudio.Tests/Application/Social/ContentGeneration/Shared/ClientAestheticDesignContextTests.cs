using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>Phase 2 (CAG2-CG-01): creatives-first design context; palette optional.</summary>
public sealed class ClientAestheticDesignContextTests
{
    [Fact]
    public void HasPalette_False_WhenPaletteNullOrEmpty()
    {
        var nullPalette = new ClientAestheticDesignContext(
            ReferenceImages: [new ProfilePostImageRef("p1", 0, "path", "hash")],
            Palette: null,
            StyleDescriptors: ["editorial"],
            Motifs: [],
            VariationSeed: "job-1");

        var emptyPalette = nullPalette with { Palette = [] };

        Assert.False(nullPalette.HasPalette);
        Assert.False(emptyPalette.HasPalette);
    }

    [Fact]
    public void HasPalette_True_WhenPaletteHasEntries()
    {
        var ctx = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#000000", null, 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "job-1");

        Assert.True(ctx.HasPalette);
    }

    [Fact]
    public void CarriesReferenceImagesAndVariationSeed()
    {
        var ctx = new ClientAestheticDesignContext(
            ReferenceImages:
            [
                new ProfilePostImageRef("p1", 0, "a", "h1"),
                new ProfilePostImageRef("p2", 0, "b", "h2"),
            ],
            Palette: null,
            StyleDescriptors: ["minimal"],
            Motifs: ["grain"],
            VariationSeed: "job-42");

        Assert.Equal(2, ctx.ReferenceImages.Count);
        Assert.Equal("job-42", ctx.VariationSeed);
    }

    // ── WU-4 (FFS-CG-02): the design context carries BrandMark ────────────────

    [Fact]
    public void CarriesBrandMark_WhenProvided()
    {
        var brandMark = new BrandMarkDetection(Present: true, PositionHint: "bottom-right", ColorHex: "#112233");

        var ctx = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: null,
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "job-1",
            BrandMark: brandMark);

        Assert.NotNull(ctx.BrandMark);
        Assert.True(ctx.BrandMark!.Present);
        Assert.Equal("bottom-right", ctx.BrandMark.PositionHint);
    }

    [Fact]
    public void BrandMark_DefaultsToNull_ExistingConstructionSitesCompileUnchanged()
    {
        var ctx = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: null,
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "job-1");

        Assert.Null(ctx.BrandMark);
    }
}
