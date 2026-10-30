using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// TDD tests for <see cref="FontCatalog"/> — the single source of truth consumed by the
/// guardrail allow-list, the picker UI, and the embedded render fonts (Content Studio editing
/// completeness, Phase 2, Decision F1/F2, task 2.1).
/// </summary>
public sealed class FontCatalogTests
{
    [Fact]
    public void For_BrandTypography_ListsBrandFontsFirst()
    {
        var entries = FontCatalog.For(new[] { "Poppins", "Poppins Bold" });

        Assert.True(entries.Count > 2, "Catalog must include brand fonts plus the system safe list.");
        Assert.Equal("Poppins", entries[0].Family);
        Assert.True(entries[0].IsBrand);
        Assert.False(entries[0].IsBundled);
        Assert.Equal("Poppins Bold", entries[1].Family);
        Assert.True(entries[1].IsBrand);

        // System entries come after all brand entries.
        var firstSystemIndex = entries.ToList().FindIndex(e => !e.IsBrand);
        Assert.Equal(2, firstSystemIndex);
    }

    [Fact]
    public void For_EmptyTypography_ReturnsSystemOnlyList()
    {
        var entries = FontCatalog.For(Array.Empty<string>());

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.False(e.IsBrand));
        Assert.All(entries, e => Assert.True(e.IsBundled));
    }

    [Fact]
    public void For_SystemSafeList_UsesRealOflNames_NoAliasingToArialOrHelvetica()
    {
        var entries = FontCatalog.For(Array.Empty<string>());

        var families = entries.Select(e => e.Family).ToArray();
        Assert.Contains("Liberation Sans", families);
        Assert.Contains("Liberation Serif", families);
        Assert.Contains("Liberation Mono", families);
        Assert.Contains("DejaVu Sans", families);
        Assert.Contains("DejaVu Serif", families);

        // Decision #1169: no aliasing to Monotype-licensed names we cannot embed.
        Assert.DoesNotContain("Arial", families);
        Assert.DoesNotContain("Helvetica", families);
    }

    [Fact]
    public void For_BlankBrandEntries_AreSkipped()
    {
        var entries = FontCatalog.For(new[] { "Poppins", "", "   ", null! });

        Assert.Single(entries, e => e.IsBrand);
    }

    [Fact]
    public void For_DuplicateBrandAndSystemFamily_KeepsBrandEntryOnly()
    {
        // A brand that (unusually) names one of the bundled system fonts as its brand font must not
        // produce two entries for the same family — the picker would show it twice.
        var entries = FontCatalog.For(new[] { "Liberation Sans" });

        Assert.Single(entries, e => e.Family.Equals("Liberation Sans", StringComparison.OrdinalIgnoreCase));
        Assert.True(entries.Single(e => e.Family == "Liberation Sans").IsBrand);
    }
}
