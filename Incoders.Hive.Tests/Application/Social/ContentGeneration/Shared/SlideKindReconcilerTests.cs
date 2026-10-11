using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// client-style-fidelity CSF-PA-06 / CSF-CG-08: a slide carrying a photographic subject on a solid
/// backdrop is an image slide, and its image reproduces the client's treatment at that position.
/// </summary>
public sealed class SlideKindReconcilerTests
{
    private static readonly SlideBackgroundTransparency ProductOnBlack =
        new(false, "product cut-out on seamless black backdrop", HasPhotographicSubject: true);

    private static readonly SlideBackgroundTransparency TextOnly =
        new(false, "solid black typographic card", HasPhotographicSubject: false);

    [Fact]
    public void FlatSlideWithASubject_BecomesAPhotoSlide()
    {
        var pattern = SlideKindReconciler.Reclassify(
            [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor],
            [null, ProductOnBlack, TextOnly],
            out var changed);

        Assert.True(changed);
        Assert.Equal([SlideBackgroundKind.Photo, SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor], pattern);
    }

    [Fact]
    public void UnmeasuredSubject_LeavesThePatternAlone()
    {
        var pattern = SlideKindReconciler.Reclassify(
            [SlideBackgroundKind.FlatColor],
            [new SlideBackgroundTransparency(false, "product on black")],
            out var changed);

        Assert.False(changed);
        Assert.Equal([SlideBackgroundKind.FlatColor], pattern);
    }

    [Fact]
    public void RatioOf_ReflectsTheCorrectedPattern()
    {
        var ratio = SlideKindReconciler.RatioOf(
            [SlideBackgroundKind.Photo, SlideBackgroundKind.Photo, SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor]);

        Assert.Equal(0.75, ratio!.Photo, precision: 5);
        Assert.Equal(0.25, ratio.FlatColor, precision: 5);
        Assert.Equal(4, ratio.SampleSize);
    }

    [Fact]
    public void Persistence_RoundTripsThePhotographicSubjectFlag()
    {
        var extraction = AestheticExtraction.Empty with
        {
            BackgroundPattern = [SlideBackgroundKind.Photo],
            BackgroundTransparency = [ProductOnBlack],
        };

        var restored = AestheticProfilePersistence.Read(AestheticProfilePersistence.Serialize(extraction, []));

        Assert.True(restored.BackgroundTransparency![0]!.HasPhotographicSubject);
    }

    // -- CSF-CG-08: the image prompt reproduces the client's treatment --------------------------

    [Fact]
    public void TreatmentCue_CarriesTheTreatment_TheBackdrop_AndTheHeadlineRoom()
    {
        var cue = ImagePromptWriter.SlideTreatmentCue(Request(
            "product cut-out on seamless black backdrop", "#0D0D0D", headingBlockWidth: 0.55));

        Assert.Contains("product cut-out on seamless black backdrop", cue);
        Assert.Contains("#0D0D0D", cue);
        Assert.Contains("left 55%", cue);
    }

    [Fact]
    public void TreatmentCue_IsEmpty_WhenNothingWasObserved()
    {
        Assert.Equal(string.Empty, ImagePromptWriter.SlideTreatmentCue(Request(null, null, headingBlockWidth: 0.55)));
    }

    private static ImagePromptRequest Request(string? treatment, string? backdrop, double headingBlockWidth) =>
        new(
            OrganizationId: Guid.NewGuid(),
            ClientId: null,
            SlideIndex: 1,
            SlideCopySummary: "El detalle detrás de tu taza",
            Aesthetic: new ClientAestheticProfile([], null, null, null,
                Typography: [new AestheticTypography(TypographyRoles.Heading, "League Gothic", 400, false, 0.09, headingBlockWidth)]),
            SlideKind: SlideBackgroundKind.Photo,
            SlideTreatment: treatment,
            BackdropHex: backdrop);
}
