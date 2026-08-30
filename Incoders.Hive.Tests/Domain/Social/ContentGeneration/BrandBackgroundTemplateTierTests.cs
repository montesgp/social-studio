using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// B1a TDD tests for BrandBackgroundTemplateSnapshot tier field mirroring.
/// Spec: BG-01 — "Snapshot mirrors domain fields".
/// Task 2.1
/// </summary>
public sealed class BrandBackgroundTemplateTierTests
{
    [Fact]
    public void BrandBackgroundTemplateSnapshot_MirrorsTierFields()
    {
        // GIVEN BackgroundEntry with Tier=Reference, FormatKey="carousel", IsFormatDefault=false
        // WHEN brand snapshot is built (simulated directly via the snapshot record)
        var snapshot = new BrandBackgroundTemplateSnapshot(
            "carousel-ref",
            "https://cdn.example.com/carousel-ref.png",
            "carousel",
            BackgroundTier.Reference,
            "carousel",
            false);

        // THEN BrandBackgroundTemplateSnapshot carries all three fields matching the domain entry
        Assert.Equal(BackgroundTier.Reference, snapshot.Tier);
        Assert.Equal("carousel", snapshot.FormatKey);
        Assert.False(snapshot.IsFormatDefault);
    }

    [Fact]
    public void BrandBackgroundTemplateSnapshot_DefaultFields_AreCreativeAndNull()
    {
        // GIVEN a snapshot built without new fields (backward-compat)
        var snapshot = new BrandBackgroundTemplateSnapshot("bg", "https://cdn.example.com/bg.png", null);

        Assert.Equal(BackgroundTier.Creative, snapshot.Tier);
        Assert.Null(snapshot.FormatKey);
        Assert.False(snapshot.IsFormatDefault);
    }
}
