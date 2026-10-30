using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// WU-3 TDD tests for <c>CompositionDefaults.ResolvePalette(brand, profile, slideIndex, inferred)</c>
/// pairing/slide-index overload (CAC-CG-02).
///
/// Verifies:
/// - Rotation formula: <c>pairings[(slideIndex-1) % count]</c> selects the correct pairing.
/// - Parity: null or empty <c>ColorPairings</c> delegates to the existing positional palette
///   so cold-start output is byte-identical to pre-change behaviour (CAC-BSP-04-a).
///
/// Refs: design §3.3, §0 OQ-2; CAC-CG-02.
/// </summary>
public sealed class CompositionDefaultsPairingTests
{
    private static readonly BrandSnapshot EmptyBrand = BrandSnapshot.FromBrandData(null, null, null);

    // -----------------------------------------------------------------------
    // Rotation formula: pairings[(slideIndex-1) % count]
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN one pairing and slideIndex=2,
    /// THEN (2-1) % 1 = 0 → the single pairing is selected.
    /// Refs: CAC-CG-02-a.
    /// </summary>
    [Fact]
    public void ResolvePalette_OnePairing_SlideIndex2_ReturnsFirstPairing()
    {
        var pairings = new List<ColorPairing> { new("#1A1A2E", "#E0E0E0", 1) };
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: pairings);
        var inferred = new InferenceAccumulator();

        var palette = CompositionDefaults.ResolvePalette(EmptyBrand, profile, slideIndex: 2, inferred);

        Assert.Equal("#1A1A2E", palette.Background);
        Assert.Equal("#E0E0E0", palette.Text);
    }

    /// <summary>
    /// GIVEN three pairings and slideIndex=4,
    /// THEN (4-1) % 3 = 0 → the first pairing is selected (rotation wraps).
    /// </summary>
    [Fact]
    public void ResolvePalette_ThreePairings_SlideIndex4_ReturnsFirstPairing()
    {
        var pairings = new List<ColorPairing>
        {
            new("#AA0000", "#FFFFFF", 1),
            new("#00AA00", "#000000", 2),
            new("#0000AA", "#FFFFFF", 3),
        };
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: pairings);
        var inferred = new InferenceAccumulator();

        var palette = CompositionDefaults.ResolvePalette(EmptyBrand, profile, slideIndex: 4, inferred);

        // (4-1) % 3 = 0 → index 0 = first pairing
        Assert.Equal("#AA0000", palette.Background);
        Assert.Equal("#FFFFFF", palette.Text);
    }

    /// <summary>
    /// GIVEN two pairings and slideIndex=2,
    /// THEN (2-1) % 2 = 1 → the second pairing is selected.
    /// </summary>
    [Fact]
    public void ResolvePalette_TwoPairings_SlideIndex2_ReturnsSecondPairing()
    {
        var pairings = new List<ColorPairing>
        {
            new("#111111", "#FFFFFF", 1),
            new("#222222", "#000000", 2),
        };
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: pairings);
        var inferred = new InferenceAccumulator();

        var palette = CompositionDefaults.ResolvePalette(EmptyBrand, profile, slideIndex: 2, inferred);

        // (2-1) % 2 = 1 → index 1 = second pairing
        Assert.Equal("#222222", palette.Background);
        Assert.Equal("#000000", palette.Text);
    }

    // -----------------------------------------------------------------------
    // Parity: null or empty ColorPairings → positional palette unchanged
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN ColorPairings=null,
    /// THEN the result is identical to the existing positional ResolvePalette(brand, inferred)
    /// (CAC-CG-02-b cold-start parity).
    /// </summary>
    [Fact]
    public void ResolvePalette_NullColorPairings_DelegatesToPositionalPalette()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: null);

        var expected = CompositionDefaults.ResolvePalette(EmptyBrand, new InferenceAccumulator());
        var actual = CompositionDefaults.ResolvePalette(EmptyBrand, profile, slideIndex: 1, new InferenceAccumulator());

        Assert.Equal(expected.Background, actual.Background);
        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal(expected.Primary, actual.Primary);
        Assert.Equal(expected.Secondary, actual.Secondary);
        Assert.Equal(expected.Accent, actual.Accent);
    }

    /// <summary>
    /// GIVEN ColorPairings=[],
    /// THEN the result is identical to the existing positional palette (empty list treated as absent).
    /// </summary>
    [Fact]
    public void ResolvePalette_EmptyColorPairings_DelegatesToPositionalPalette()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: []);

        var expected = CompositionDefaults.ResolvePalette(EmptyBrand, new InferenceAccumulator());
        var actual = CompositionDefaults.ResolvePalette(EmptyBrand, profile, slideIndex: 3, new InferenceAccumulator());

        Assert.Equal(expected.Background, actual.Background);
        Assert.Equal(expected.Text, actual.Text);
    }

    /// <summary>
    /// GIVEN profile=null entirely,
    /// THEN the result is identical to the positional palette.
    /// </summary>
    [Fact]
    public void ResolvePalette_NullProfile_DelegatesToPositionalPalette()
    {
        var expected = CompositionDefaults.ResolvePalette(EmptyBrand, new InferenceAccumulator());
        var actual = CompositionDefaults.ResolvePalette(EmptyBrand, profile: null, slideIndex: 1, new InferenceAccumulator());

        Assert.Equal(expected.Background, actual.Background);
        Assert.Equal(expected.Text, actual.Text);
    }
}
