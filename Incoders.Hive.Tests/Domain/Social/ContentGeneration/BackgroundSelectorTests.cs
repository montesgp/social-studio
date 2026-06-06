using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// B1a TDD tests for BackgroundSelector tier-aware selection.
/// Spec: BG-03 — Tier-Aware Background Selection (all four scenarios).
/// Task 2.2
/// </summary>
public sealed class BackgroundSelectorTests
{
    // -----------------------------------------------------------------------
    // BG-03 Scenario 1: Tier-1 ForcedDefault returned without generation
    // -----------------------------------------------------------------------

    [Fact]
    public void Tier1_ForcedDefault_ReturnedWithoutGeneration()
    {
        // GIVEN snapshot has Tier=ForcedDefault, FormatKey="flyer", IsFormatDefault=true
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot(
                "flyer-bg", "https://cdn.example.com/flyer-bg.png", null,
                BackgroundTier.ForcedDefault, "flyer", true)
        };

        // WHEN SelectBackgroundTemplate("flyer") called
        var result = BackgroundSelector.Select(templates, "flyer");

        // THEN result is BackgroundSelection.ForcedDefault carrying that Url
        var forced = Assert.IsType<BackgroundSelection.ForcedDefault>(result);
        Assert.Equal("https://cdn.example.com/flyer-bg.png", forced.Url);
        Assert.Equal("flyer-bg", forced.Name);
    }

    // -----------------------------------------------------------------------
    // BG-03 Scenario 2: Tier-2 reference list returned when no forced default exists
    // -----------------------------------------------------------------------

    [Fact]
    public void Tier2_References_ReturnedWhenNoForcedDefault()
    {
        // GIVEN snapshot has two Reference entries, no ForcedDefault
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("ref1", "https://cdn.example.com/ref1.png", null,
                BackgroundTier.Reference, null, false),
            new BrandBackgroundTemplateSnapshot("ref2", "https://cdn.example.com/ref2.png", null,
                BackgroundTier.Reference, null, false),
        };

        // WHEN SelectBackgroundTemplate("flyer") called
        var result = BackgroundSelector.Select(templates, "flyer");

        // THEN result is BackgroundSelection.References carrying both entries
        var refs = Assert.IsType<BackgroundSelection.References>(result);
        Assert.Equal(2, refs.Entries.Count);
    }

    // -----------------------------------------------------------------------
    // BG-03 Scenario 3: Tier-3 creative returned when no structured entries exist
    // -----------------------------------------------------------------------

    [Fact]
    public void Tier3_Creative_WhenNoStructuredEntries()
    {
        // GIVEN snapshot has no ForcedDefault and no Reference entries (Creative only)
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("creative-bg", "https://cdn.example.com/creative.png", null,
                BackgroundTier.Creative, null, false),
        };

        // WHEN SelectBackgroundTemplate("flyer") called
        var result = BackgroundSelector.Select(templates, "flyer");

        // THEN result is BackgroundSelection.Creative
        Assert.IsType<BackgroundSelection.Creative>(result);
    }

    // -----------------------------------------------------------------------
    // BG-03 Scenario 4: Forced default for different format does not match
    // -----------------------------------------------------------------------

    [Fact]
    public void ForcedDefault_DifferentFormat_DoesNotMatch()
    {
        // GIVEN ForcedDefault exists for FormatKey="carousel"
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("carousel-bg", "https://cdn.example.com/carousel-bg.png", null,
                BackgroundTier.ForcedDefault, "carousel", true),
        };

        // WHEN SelectBackgroundTemplate("flyer") called
        var result = BackgroundSelector.Select(templates, "flyer");

        // THEN result is NOT ForcedDefault; carousel default is not used for flyer
        Assert.IsNotType<BackgroundSelection.ForcedDefault>(result);
    }
}
