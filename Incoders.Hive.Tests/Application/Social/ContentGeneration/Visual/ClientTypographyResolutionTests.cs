using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// Per-client typography.
///
/// <para>
/// <c>ResolveTypography</c> used to take the FIRST brand typography entry, apply it to headings AND
/// body alike, and hardcode the sizes to 28/16 px. Every client therefore rendered at the same two
/// sizes in one family. These tests pin the observed-typography path: role-aware families, weights,
/// italics, and sizes derived from the client's measured proportion of canvas height — while keeping
/// the old behaviour byte-identical for any client without observed typography.
/// </para>
/// </summary>
public sealed class ClientTypographyResolutionTests
{
    private const int CarouselHeight = 1350;

    [Fact]
    public void SizesScaleWithCanvasHeight_SoOneProfileDrivesCarouselAndStoryAlike()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.03));

        var carousel = Resolve(profile, CarouselHeight);
        var story = Resolve(profile, 1920);

        Assert.Equal(108, carousel.HeadingSize);   // 0.08 * 1350
        Assert.Equal(41, carousel.BodySize);       // 0.03 * 1350 -> 40.5 rounds to 41
        Assert.Equal(154, story.HeadingSize);      // 0.08 * 1920 -> 153.6 rounds to 154
    }

    [Fact]
    public void HeadingAndBodyResolveIndependently_IncludingWeightAndItalic()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Serif", 400, true, 0.025));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal("Liberation Sans", typography.HeadingFamily);
        Assert.Equal("Liberation Serif", typography.BodyFamily);
        Assert.Equal(800, typography.HeadingWeight);
        Assert.Equal(400, typography.BodyWeight);
        Assert.True(typography.BodyItalic);
    }

    [Fact]
    public void AnUnbundledFamilySubstitutesByClassification_AndReportsTheSubstitution()
    {
        // Playfair Display is a serif the renderer has no binary for. It must fall back to a SERIF,
        // never to the sans default, or the slide changes character entirely.
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Playfair Display", 700, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Poppins", 400, false, 0.03));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal("Liberation Serif", typography.HeadingFamily);
        Assert.Equal("Liberation Sans", typography.BodyFamily);

        // The gap is reported rather than hidden.
        Assert.Equal("Playfair Display", typography.RequestedHeadingFamily);
        Assert.Equal("Poppins", typography.RequestedBodyFamily);
    }

    [Fact]
    public void ABundledFamilyIsNotReportedAsASubstitution()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 700, false, 0.08));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Null(typography.RequestedHeadingFamily);
    }

    [Theory]
    [InlineData(0)]      // never measured
    [InlineData(-0.05)]  // nonsense
    [InlineData(0.9)]    // a "headline" 90% of the canvas is a measurement error
    public void AnUnusableSizeRatioKeepsTheExistingDefault(double ratio)
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 700, false, ratio));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal(28, typography.HeadingSize);
    }

    [Fact]
    public void TinyMeasurementsAreFlooredSoCopyStaysLegible()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.001));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal(12, typography.BodySize);   // 1.35px floored to the legibility minimum
    }

    [Fact]
    public void NoObservedTypography_IsByteIdenticalToTheLegacyDefaults()
    {
        var legacy = CompositionDefaults.ResolveTypography(EmptyBrand(), new InferenceAccumulator());
        var viaProfile = Resolve(profile: null, CarouselHeight);

        Assert.Equal(legacy, viaProfile);
    }

    [Fact]
    public void AProfileNamingNeitherHeadingNorBody_FallsBackRatherThanInvent()
    {
        // Only an accent role — nothing the heading/body slots can use.
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Accent, "Liberation Serif", 400, true, 0.03));

        var legacy = CompositionDefaults.ResolveTypography(EmptyBrand(), new InferenceAccumulator());

        Assert.Equal(legacy, Resolve(profile, CarouselHeight));
    }

    // ── D4: caption/overline role ───────────────────────────────────────────

    [Fact]
    public void CaptionRole_Present_ResolvesFamilySizeWeightItalic()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.03),
            new AestheticTypography(TypographyRoles.Caption, "Liberation Serif", 700, true, 0.02));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal("Liberation Serif", typography.CaptionFamily);
        Assert.Equal(27, typography.CaptionSize); // 0.02 * 1350 = 27
        Assert.Equal(700, typography.CaptionWeight);
        Assert.True(typography.CaptionItalic);
        Assert.Equal("Liberation Serif", typography.EffectiveCaptionFamily);
    }

    [Fact]
    public void CaptionRole_Absent_FallsBackToBodyViaEffectiveCaptionFamily()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Serif", 400, false, 0.03));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Null(typography.CaptionFamily);
        Assert.Null(typography.CaptionSize);
        Assert.Equal(800, typography.CaptionWeight); // default, matches today's hardcoded cta weight
        Assert.False(typography.CaptionItalic);
        Assert.Equal("Liberation Serif", typography.EffectiveCaptionFamily); // == BodyFamily
    }

    [Fact]
    public void OverlineRole_IsAnAliasForCaption()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.03),
            new AestheticTypography(TypographyRoles.Overline, "Liberation Mono", 600, false, 0.018));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal("Liberation Mono", typography.CaptionFamily);
        Assert.Equal(600, typography.CaptionWeight);
    }

    [Fact]
    public void AccentRole_IsTheLegacySynonymForCaption_WhenNoCaptionOrOverlinePresent()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.03),
            new AestheticTypography(TypographyRoles.Accent, "Liberation Serif", 500, true, 0.02));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal("Liberation Serif", typography.CaptionFamily);
        Assert.Equal(500, typography.CaptionWeight);
        Assert.True(typography.CaptionItalic);
    }

    [Fact]
    public void CaptionRoleAlone_WithoutHeadingOrBody_StillEarlyOutsToLegacyDefaults()
    {
        // Caption alone never activates the profile-aware branch — matches the existing
        // "heading and body both absent" early-out per D4.
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Caption, "Liberation Serif", 700, true, 0.02));

        var legacy = CompositionDefaults.ResolveTypography(EmptyBrand(), new InferenceAccumulator());

        Assert.Equal(legacy, Resolve(profile, CarouselHeight));
    }

    // ── FontFamilyResolution ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Playfair Display", "Liberation Serif")]
    [InlineData("Merriweather", "Liberation Serif")]
    [InlineData("Poppins", "Liberation Sans")]
    [InlineData("Open Sans", "Liberation Sans")]
    [InlineData("JetBrains Mono", "Liberation Mono")]
    [InlineData("", "Liberation Sans")]
    public void ClassificationPicksALikeForLikeSubstitute(string detected, string expected)
    {
        Assert.Equal(expected, FontFamilyResolution.Resolve(detected).Renderable);
    }

    [Fact]
    public void SansSerifNamesResolveToSans_NotSerif()
    {
        // "sans serif" contains "serif"; a naive token check sends every sans family to a serif.
        Assert.Equal("Liberation Sans", FontFamilyResolution.Resolve("Helvetica Sans Serif").Renderable);
    }

    [Fact]
    public void NeverAliasesToAMonotypeLicensedFamily()
    {
        // Decision #1169: Arial/Helvetica cannot be legally embedded, so they must never come back
        // as a renderable family no matter what the extractor reports.
        foreach (var detected in new[] { "Arial", "Helvetica", "Helvetica Neue" })
        {
            var renderable = FontFamilyResolution.Resolve(detected).Renderable;
            Assert.DoesNotContain("Arial", renderable, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Helvetica", renderable, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProfileRoundTrip_PreservesObservedTypography()
    {
        var extraction = AestheticExtraction.Empty with
        {
            Typography =
            [
                new AestheticTypography(TypographyRoles.Heading, "Poppins", 800, false, 0.08),
                new AestheticTypography(TypographyRoles.Body, "Lora", 400, true, 0.028),
            ],
        };

        var restored = AestheticProfilePersistence.Read(
            AestheticProfilePersistence.Serialize(extraction, []));

        Assert.NotNull(restored.Typography);
        Assert.Equal(2, restored.Typography!.Count);
        Assert.Equal("Poppins", restored.Typography[0].FontFamily);
        Assert.Equal(800, restored.Typography[0].Weight);
        Assert.True(restored.Typography[1].Italic);
        Assert.Equal(0.028, restored.Typography[1].SizeRatio, precision: 5);
    }

    [Fact]
    public void BlobsWrittenBeforeTypographyExisted_ReadBackAsNull()
    {
        const string legacyJson =
            """{"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,"referenceImages":[]}""";

        Assert.Null(AestheticProfilePersistence.Read(legacyJson).Typography);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ClientAestheticProfile ProfileWith(params AestheticTypography[] entries) =>
        new(BackgroundDescriptors: [], VoiceTone: null, AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null, Typography: entries);

    private static CompositionTypography Resolve(ClientAestheticProfile? profile, int canvasHeight) =>
        CompositionDefaults.ResolveTypography(EmptyBrand(), profile, canvasHeight, new InferenceAccumulator());

    /// <summary>A brand with no manual at all, so the legacy path yields the hardcoded defaults.</summary>
    private static BrandSnapshot EmptyBrand() =>
        new(Client: null, Brand: null, Strategy: null, MonthlyObjective: null,
            ReferentAccounts: [], CompetitorAccounts: [], InferredFields: new Dictionary<string, bool>());
}
