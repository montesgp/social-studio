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
        // Merriweather is a serif the renderer has no binary for. It must fall back to a SERIF,
        // never to the sans default, or the slide changes character entirely.
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Merriweather", 700, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Gotham", 400, false, 0.03));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal("Lora", typography.HeadingFamily);
        Assert.Equal("Inter", typography.BodyFamily);

        // The gap is reported rather than hidden.
        Assert.Equal("Merriweather", typography.RequestedHeadingFamily);
        Assert.Equal("Gotham", typography.RequestedBodyFamily);
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

    // ── client-aesthetic-generation-hardening D1: nullable measured heading/body size ──────────

    [Fact]
    public void MeasuredPresent_ResolvesMeasuredHeadingAndBodySize()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.03));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal(108, typography.MeasuredHeadingSize); // 0.08 * 1350
        Assert.Equal(41, typography.MeasuredBodySize);      // 0.03 * 1350 -> 40.5 rounds to 41
    }

    [Fact]
    public void MeasuredAbsent_LeavesMeasuredSizesNull()
    {
        var legacy = CompositionDefaults.ResolveTypography(EmptyBrand(), new InferenceAccumulator());

        Assert.Null(legacy.MeasuredHeadingSize);
        Assert.Null(legacy.MeasuredBodySize);
        Assert.Null(Resolve(profile: null, CarouselHeight).MeasuredHeadingSize);
        Assert.Null(Resolve(profile: null, CarouselHeight).MeasuredBodySize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.05)]
    [InlineData(0.9)]
    public void MeasuredHeadingSize_UnusableSizeRatioResolvesNull(double ratio)
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 700, false, ratio),
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.03));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Null(typography.MeasuredHeadingSize);
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
    [InlineData("Playfair Display", "Playfair Display", true)]
    [InlineData("Poppins", "Poppins", true)]
    [InlineData("Open Sans", "Open Sans", true)]
    [InlineData("JetBrains Mono", "JetBrains Mono", true)]
    [InlineData("league gothic", "League Gothic", true)]
    [InlineData("", "Liberation Sans", false)]
    public void BundledCatalogFamiliesRenderAsThemselves(string detected, string expected, bool exact)
    {
        var resolved = FontFamilyResolution.Resolve(detected);

        Assert.Equal(expected, resolved.Renderable);
        Assert.Equal(exact, resolved.IsExact);
    }

    [Theory]
    // A "Name / description" answer names the family in its first token.
    [InlineData("Anton / heavy condensed grotesque", "Anton")]
    [InlineData("Poppins / geometric sans", "Poppins")]
    // A bundled name inside a longer style name.
    [InlineData("Poppins SemiBold", "Poppins")]
    // The longest bundled name wins, so the condensed cut is not mistaken for the regular one.
    [InlineData("Roboto Condensed Bold", "Roboto Condensed")]
    public void NamedFamiliesAreFoundInsideDescriptiveAnswers(string detected, string expected)
    {
        var resolved = FontFamilyResolution.Resolve(detected);

        Assert.Equal(expected, resolved.Renderable);
        Assert.True(resolved.IsExact);
    }

    [Theory]
    [InlineData("Merriweather", "Lora")]
    [InlineData("monospace (typewriter)", "Courier Prime")]
    [InlineData("some mono font", "Space Mono")]
    [InlineData("Bebas Neue Pro condensed", "Bebas Neue")]
    [InlineData("tall condensed grotesque", "Oswald")]
    [InlineData("heavy condensed grotesque", "Anton")]
    [InlineData("Gotham geometric", "Poppins")]
    [InlineData("handwritten marker", "Caveat")]
    [InlineData("Didone display serif", "Playfair Display")]
    public void UnbundledFamiliesSubstituteLikeForLike(string detected, string expected)
    {
        Assert.Equal(expected, FontFamilyResolution.Resolve(detected).Renderable);
    }

    [Fact]
    public void SansSerifNamesResolveToSans_NotSerif()
    {
        // "sans serif" contains "serif"; a naive token check sends every sans family to a serif.
        Assert.Equal("Inter", FontFamilyResolution.Resolve("Helvetica Sans Serif").Renderable);
    }

    [Theory]
    [InlineData(0.5, 540)]     // the client's measured block
    [InlineData(0.1, 324)]     // implausibly narrow -> clamped to 30 %
    [InlineData(0.98, 972)]    // edge-to-edge -> clamped to 90 %
    public void HeadingBlockWidthFollowsTheMeasuredRatio_Clamped(double ratio, int expectedWidth)
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "League Gothic", 400, false, 0.08, BlockWidthRatio: ratio));

        var typography = CompositionDefaults.ResolveTypography(
            EmptyBrand(), profile, CarouselHeight, new InferenceAccumulator(), canvasWidth: 1080);

        Assert.Equal(expectedWidth, typography.MeasuredHeadingBlockWidth);
    }

    [Fact]
    public void HeadingBlockWidth_Unmeasured_StaysNull()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "League Gothic", 400, false, 0.08));

        var typography = CompositionDefaults.ResolveTypography(
            EmptyBrand(), profile, CarouselHeight, new InferenceAccumulator(), canvasWidth: 1080);

        Assert.Null(typography.MeasuredHeadingBlockWidth);
    }

    [Fact]
    public void ObservedWeightIsClampedToTheWeightsTheFamilyShips()
    {
        // League Gothic ships a single 400 face: asking for the extractor's 800 would make Chromium
        // synthesise a fake bold, which is how a condensed headline rendered wide and heavy.
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "League Gothic", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Poppins", 350, false, 0.03));

        var typography = Resolve(profile, CarouselHeight);

        Assert.Equal("League Gothic", typography.HeadingFamily);
        Assert.Equal(400, typography.MeasuredHeadingWeight);
        Assert.Equal(400, typography.HeadingWeight);
        // Poppins ships static 300/400/...: 350 snaps to the nearest real face.
        Assert.Contains(typography.MeasuredBodyWeight, new int?[] { 300, 400 });
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
