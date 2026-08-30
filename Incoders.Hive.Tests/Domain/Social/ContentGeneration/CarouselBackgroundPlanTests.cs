using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// TDD tests for WU-2: CarouselBackgroundPlan value object.
/// Refs: design §3.5; OQ-1/OQ-5; tasks 2.6.
/// </summary>
public sealed class CarouselBackgroundPlanTests
{
    private static readonly BackgroundSelection Creative = new BackgroundSelection.Creative();

    // -----------------------------------------------------------------------
    // ForSlide: 1-based indexing
    // -----------------------------------------------------------------------

    [Fact]
    public void ForSlide_1Based_ReturnsFirstSlideForIndex1()
    {
        var slides = new BackgroundSelection[]
        {
            new BackgroundSelection.Creative(),
            new BackgroundSelection.ForcedDefault("url2", "bg2"),
        };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.AiPalette, slides, "seed");

        var result = plan.ForSlide(1);

        Assert.IsType<BackgroundSelection.Creative>(result);
    }

    [Fact]
    public void ForSlide_Index2_ReturnsSecondSlide()
    {
        var slides = new BackgroundSelection[]
        {
            new BackgroundSelection.Creative(),
            new BackgroundSelection.ForcedDefault("url2", "bg2"),
        };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.AiPalette, slides, "seed");

        var result = plan.ForSlide(2);

        var fd = Assert.IsType<BackgroundSelection.ForcedDefault>(result);
        Assert.Equal("url2", fd.Url);
    }

    // -----------------------------------------------------------------------
    // ForSlide: overflow clamps to last entry
    // -----------------------------------------------------------------------

    [Fact]
    public void ForSlide_OverflowIndex_ClampsToLastEntry()
    {
        var slides = new BackgroundSelection[]
        {
            new BackgroundSelection.Creative(),
            new BackgroundSelection.ForcedDefault("url-last", "last"),
        };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.AiPalette, slides, "seed");

        var result = plan.ForSlide(99);

        var fd = Assert.IsType<BackgroundSelection.ForcedDefault>(result);
        Assert.Equal("url-last", fd.Url);
    }

    [Fact]
    public void ForSlide_ZeroIndex_ClampsToFirstEntry()
    {
        var slides = new BackgroundSelection[]
        {
            new BackgroundSelection.ForcedDefault("url-first", "first"),
            new BackgroundSelection.Creative(),
        };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.AiPalette, slides, "seed");

        var result = plan.ForSlide(0);

        var fd = Assert.IsType<BackgroundSelection.ForcedDefault>(result);
        Assert.Equal("url-first", fd.Url);
    }

    // -----------------------------------------------------------------------
    // IsCoherent: true when all entries share same type
    // -----------------------------------------------------------------------

    [Fact]
    public void IsCoherent_WhenAllEntriesAreCreative_ReturnsTrue()
    {
        var slides = new BackgroundSelection[]
        {
            new BackgroundSelection.Creative(),
            new BackgroundSelection.Creative(),
            new BackgroundSelection.Creative(),
        };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.AiPalette, slides, "seed");

        Assert.True(plan.IsCoherent);
    }

    [Fact]
    public void IsCoherent_WhenEntriesMixTypes_ReturnsFalse()
    {
        var slides = new BackgroundSelection[]
        {
            new BackgroundSelection.Creative(),
            new BackgroundSelection.ForcedDefault("url", "bg"),
        };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.AiPalette, slides, "seed");

        Assert.False(plan.IsCoherent);
    }

    [Fact]
    public void IsCoherent_SingleEntry_ReturnsTrue()
    {
        var slides = new BackgroundSelection[] { new BackgroundSelection.Creative() };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.AiPalette, slides, "seed");

        Assert.True(plan.IsCoherent);
    }

    // -----------------------------------------------------------------------
    // StableHash: deterministic rotation — same seed+index always same result
    // -----------------------------------------------------------------------

    [Fact]
    public void StableHash_SameSeedAndIndex_ReturnsSameValue()
    {
        var seed = "job-123";
        var index = 2;

        var h1 = CarouselBackgroundPlan.StableHash(seed, index);
        var h2 = CarouselBackgroundPlan.StableHash(seed, index);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void StableHash_DifferentSlideIndex_GivesDifferentHashForTypicalInputs()
    {
        var seed = "job-abc";

        // With 3-item set, at least one pair of consecutive indices must rotate to different slots
        var set = Enumerable.Range(0, 5)
            .Select(i => CarouselBackgroundPlan.StableHash(seed, i) % 3)
            .ToList();

        // Not all slides should map to the same slot (varied rotation)
        Assert.False(set.All(s => s == set[0]),
            "Expected deterministic rotation to produce different slot assignments across slides");
    }

    [Fact]
    public void StableHash_ReturnsNonNegativeValue()
    {
        var hash = CarouselBackgroundPlan.StableHash("any-seed", 0);

        Assert.True(hash >= 0);
    }

    [Theory]
    [InlineData("job-1", 0)]
    [InlineData("job-2", 1)]
    [InlineData("job-3", 7)]
    public void StableHash_IsReproducible(string seed, int index)
    {
        var h1 = CarouselBackgroundPlan.StableHash(seed, index);
        var h2 = CarouselBackgroundPlan.StableHash(seed, index);

        Assert.Equal(h1, h2);
    }

    // -----------------------------------------------------------------------
    // WU-8.4: WithForcedDefaults — overlays ForcedDefault onto creative slots
    // -----------------------------------------------------------------------

    [Fact]
    public void WithForcedDefaults_OverridesSpecifiedSlides_LeavesOthersUnchanged()
    {
        // GIVEN a 5-slide plan where all slides are ClientAesthetic
        var perSlide = Enumerable.Range(1, 5)
            .Select(_ => (BackgroundSelection) new BackgroundSelection.Creative())
            .ToList();
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.ClientAesthetic, perSlide, "seed");

        // AND a map that supplies ForcedDefault for slides 1 and 5
        var map = new Dictionary<int, BackgroundSelection.ForcedDefault>
        {
            [1] = new BackgroundSelection.ForcedDefault("https://storage.example.com/slide1.png", "ai-generated-background"),
            [5] = new BackgroundSelection.ForcedDefault("https://storage.example.com/slide5.png", "ai-generated-background"),
        };

        // WHEN WithForcedDefaults is applied
        var updated = plan.WithForcedDefaults(map);

        // THEN slide 1 and 5 have ForcedDefault
        var s1 = updated.ForSlide(1);
        Assert.IsType<BackgroundSelection.ForcedDefault>(s1);
        Assert.Equal("https://storage.example.com/slide1.png", ((BackgroundSelection.ForcedDefault)s1).Url);

        var s5 = updated.ForSlide(5);
        Assert.IsType<BackgroundSelection.ForcedDefault>(s5);
        Assert.Equal("https://storage.example.com/slide5.png", ((BackgroundSelection.ForcedDefault)s5).Url);

        // AND slides 2, 3, 4 remain Creative (unchanged)
        Assert.IsType<BackgroundSelection.Creative>(updated.ForSlide(2));
        Assert.IsType<BackgroundSelection.Creative>(updated.ForSlide(3));
        Assert.IsType<BackgroundSelection.Creative>(updated.ForSlide(4));
    }

    [Fact]
    public void WithForcedDefaults_EmptyMap_ReturnsPlanUnchanged()
    {
        var perSlide = new BackgroundSelection[]
        {
            new BackgroundSelection.Creative(),
            new BackgroundSelection.Creative(),
        };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.ClientAesthetic, perSlide, "seed");
        var map = new Dictionary<int, BackgroundSelection.ForcedDefault>();

        var updated = plan.WithForcedDefaults(map);

        Assert.IsType<BackgroundSelection.Creative>(updated.ForSlide(1));
        Assert.IsType<BackgroundSelection.Creative>(updated.ForSlide(2));
    }

    [Fact]
    public void WithForcedDefaults_OutOfRangeIndex_IsIgnored()
    {
        // GIVEN a 2-slide plan
        var perSlide = new BackgroundSelection[]
        {
            new BackgroundSelection.Creative(),
            new BackgroundSelection.Creative(),
        };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.ClientAesthetic, perSlide, "seed");

        // AND a map with index 9 (out of range)
        var map = new Dictionary<int, BackgroundSelection.ForcedDefault>
        {
            [9] = new BackgroundSelection.ForcedDefault("https://example.com/bad.png", "ai-generated-background")
        };

        // WHEN — out-of-range index is silently ignored, plan unchanged
        var updated = plan.WithForcedDefaults(map);

        Assert.IsType<BackgroundSelection.Creative>(updated.ForSlide(1));
        Assert.IsType<BackgroundSelection.Creative>(updated.ForSlide(2));
    }

    [Fact]
    public void WithForcedDefaults_DoesNotMutateOriginalPlan()
    {
        var perSlide = new BackgroundSelection[] { new BackgroundSelection.Creative() };
        var plan = new CarouselBackgroundPlan(BackgroundSourcingPolicy.ClientAesthetic, perSlide, "seed");
        var map = new Dictionary<int, BackgroundSelection.ForcedDefault>
        {
            [1] = new BackgroundSelection.ForcedDefault("https://example.com/s1.png", "ai-generated-background")
        };

        plan.WithForcedDefaults(map);

        // Original plan must not be mutated (immutable record)
        Assert.IsType<BackgroundSelection.Creative>(plan.ForSlide(1));
    }
}
