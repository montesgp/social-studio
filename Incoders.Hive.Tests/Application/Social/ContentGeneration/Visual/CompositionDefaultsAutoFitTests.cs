using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// content-layout-and-image-fidelity WU-5 (CLIF-CG-07): copy-length-aware typography auto-fit.
///
/// <para>
/// WU-4's reflow (<c>MeasuredLayoutReflow</c>) resolves overlap by repositioning SUBSEQUENT slots
/// from the preceding slot's measured extent — but it cannot help a headline slot that is itself
/// clamped against the canvas bottom (<c>MeasuredLayoutReflow</c>'s own clamp comment: "that case
/// is WU-5's auto-fit"), nor does it reduce a single slot's OWN rendered height when nothing sits
/// below it to push. This is a genuinely separate concern from WU-4's positional reflow: an
/// unusually long copy string at a legitimate, real measured <c>SizeRatio</c> should also get a
/// bounded font-size reduction, never below a stated floor and never an increase.
/// </para>
/// </summary>
public sealed class CompositionDefaultsAutoFitTests
{
    private const int CarouselHeight = 1350;

    [Fact]
    public void UnusuallyLongCopy_ReducesResolvedHeadingSize_WithinStatedFloor()
    {
        var profile = ProfileWith(new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.10));
        var unfit = Resolve(profile, CarouselHeight, headlineCopy: null).HeadingSize; // 135px (0.10 * 1350)

        var longCopy = new string('x', 200); // materially longer than typical
        var fitted = Resolve(profile, CarouselHeight, headlineCopy: longCopy).HeadingSize;

        Assert.Equal(135, unfit);
        Assert.True(fitted < unfit, $"Expected auto-fit to reduce {unfit}px for a 200-char headline, got {fitted}px.");
        Assert.True(fitted >= 90, $"Auto-fit must never drop below its stated floor; got {fitted}px.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(39)]   // below the auto-fit length threshold
    [InlineData(40)]   // exactly the threshold
    [InlineData(80)]
    [InlineData(200)]
    [InlineData(1000)]
    public void AutoFit_NeverIncreasesTheResolvedSize_ForAnyCopyLength(int copyLength)
    {
        var profile = ProfileWith(new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08));
        var unfit = Resolve(profile, CarouselHeight, headlineCopy: null).HeadingSize;

        var copy = copyLength == 0 ? string.Empty : new string('x', copyLength);
        var fitted = Resolve(profile, CarouselHeight, headlineCopy: copy).HeadingSize;

        Assert.True(fitted <= unfit, $"Auto-fit must never increase size: unfit={unfit}, fitted({copyLength} chars)={fitted}.");
    }

    [Fact]
    public void CopyThatAlreadyFitsAtItsMeasuredRatio_RendersByteIdentically_NoOpRegression()
    {
        // Ties to CLIF-CG-08: a profile/copy combination that already fits today must not change.
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.03));

        var withoutCopy = Resolve(profile, CarouselHeight, headlineCopy: null);
        var withShortCopy = Resolve(profile, CarouselHeight, headlineCopy: "Short headline");

        Assert.Equal(withoutCopy, withShortCopy);
    }

    [Fact]
    public void ExistingCallersWithoutHeadlineCopy_AreUnaffected_ByteIdenticalToPreWu5()
    {
        var profile = ProfileWith(
            new AestheticTypography(TypographyRoles.Heading, "Liberation Sans", 800, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Liberation Sans", 400, false, 0.03));

        var legacyCallShape = CompositionDefaults.ResolveTypography(
            EmptyBrand(), profile, CarouselHeight, new InferenceAccumulator());
        var explicitNullCopy = Resolve(profile, CarouselHeight, headlineCopy: null);

        Assert.Equal(legacyCallShape, explicitNullCopy);
    }

    private static ClientAestheticProfile ProfileWith(params AestheticTypography[] entries) =>
        new(BackgroundDescriptors: [], VoiceTone: null, AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null, Typography: entries);

    private static CompositionTypography Resolve(ClientAestheticProfile? profile, int canvasHeight, string? headlineCopy) =>
        CompositionDefaults.ResolveTypography(EmptyBrand(), profile, canvasHeight, new InferenceAccumulator(), headlineCopy);

    /// <summary>A brand with no manual at all, so the legacy path yields the hardcoded defaults.</summary>
    private static BrandSnapshot EmptyBrand() =>
        new(Client: null, Brand: null, Strategy: null, MonthlyObjective: null,
            ReferentAccounts: [], CompetitorAccounts: [], InferredFields: new Dictionary<string, bool>());
}
