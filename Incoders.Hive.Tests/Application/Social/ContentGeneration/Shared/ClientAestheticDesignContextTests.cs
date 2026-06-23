using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

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
}
