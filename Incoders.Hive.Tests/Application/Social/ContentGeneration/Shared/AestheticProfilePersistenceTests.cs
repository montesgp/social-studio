using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.11 [RED] — <see cref="AestheticProfilePersistence"/> round-trip for the three
/// new client-aligned-carousel-generation fields.
/// Refs: design §3.1/§7; tasks 1.11/1.12; CAC-BSP-02-b, CAC-BSP-02-c.
/// </summary>
public sealed class AestheticProfilePersistenceTests
{
    // ── Round-trip: Write then Read preserves new fields (CAC-BSP-02-c) ───────

    [Fact]
    public void Serialize_ThenRead_PreservesBackgroundPattern()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BackgroundPattern: [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.Photo]);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.BackgroundPattern);
        Assert.Equal(3, read.BackgroundPattern!.Count);
        Assert.Equal(SlideBackgroundKind.Photo, read.BackgroundPattern[0]);
        Assert.Equal(SlideBackgroundKind.FlatColor, read.BackgroundPattern[1]);
        Assert.Equal(SlideBackgroundKind.Photo, read.BackgroundPattern[2]);
    }

    [Fact]
    public void Serialize_ThenRead_PreservesTypicalSlideCount_DerivedFromPattern()
    {
        // TypicalSlideCount is derived from BackgroundPattern.Count at serialize time
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BackgroundPattern: [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor, SlideBackgroundKind.Photo]);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Equal(4, read.TypicalSlideCount); // length of BackgroundPattern
    }

    [Fact]
    public void Serialize_ThenRead_PreservesColorPairings()
    {
        IReadOnlyList<ColorPairing> pairings =
        [
            new ColorPairing("#1A1A2E", "#E0E0E0", 1),
            new ColorPairing("#16213E", "#FFFFFF", 2),
        ];

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            ColorPairings: pairings);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.ColorPairings);
        Assert.Equal(2, read.ColorPairings!.Count);
        Assert.Equal("#1A1A2E", read.ColorPairings[0].BackgroundHex);
        Assert.Equal("#E0E0E0", read.ColorPairings[0].TextHex);
        Assert.Equal(1, read.ColorPairings[0].Rank);
        Assert.Equal("#16213E", read.ColorPairings[1].BackgroundHex);
    }

    // ── Old blobs without new keys → null, no exception (CAC-BSP-02-b cold-start) ──

    [Fact]
    public void Read_OldBlobWithoutNewKeys_ReturnsNullForNewFields_NoException()
    {
        // JSON from before client-aligned-carousel-generation landed
        const string oldJson =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[]}""";

        var read = AestheticProfilePersistence.Read(oldJson);

        Assert.Null(read.BackgroundPattern);
        Assert.Null(read.TypicalSlideCount);
        Assert.Null(read.ColorPairings);
        // Existing fields still work
        Assert.NotNull(read.StyleDescriptors);
        Assert.Contains("editorial", read.StyleDescriptors!);
    }

    [Fact]
    public void Read_NullJson_ReturnsAllNulls_NoException()
    {
        var read = AestheticProfilePersistence.Read(null);

        Assert.Null(read.BackgroundPattern);
        Assert.Null(read.TypicalSlideCount);
        Assert.Null(read.ColorPairings);
        Assert.Null(read.Palette);
    }

    // ── Null BackgroundPattern → no typicalSlideCount written ─────────────────

    [Fact]
    public void Serialize_NullBackgroundPattern_TypicalSlideCount_IsNull()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null
            // BackgroundPattern and ColorPairings default to null
        );

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Null(read.BackgroundPattern);
        Assert.Null(read.TypicalSlideCount);
        Assert.Null(read.ColorPairings);
    }

    // ── Existing fields still round-trip correctly alongside new fields ────────

    [Fact]
    public void Serialize_ExistingPaletteAndRefs_StillRoundTripCorrectly_WithNewFields()
    {
        var extraction = new AestheticExtraction(
            Palette: [new AestheticPaletteEntry("#C0FFEE", "Primary", 1)],
            StyleDescriptors: ["airy"],
            Motifs: ["film grain"],
            CaptionVoice: "playful",
            BackgroundPattern: [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor],
            ColorPairings: [new ColorPairing("#C0FFEE", "#000000", 1)]);

        var refs = new[] { new ProfilePostImageRef("p1", 0, "org/client/p1.jpg", "hash1") };

        var json = AestheticProfilePersistence.Serialize(extraction, refs);
        var read = AestheticProfilePersistence.Read(json);

        // Existing fields
        Assert.NotNull(read.Palette);
        Assert.Single(read.Palette!);
        Assert.Equal("#C0FFEE", read.Palette![0].Hex);
        Assert.Equal("airy", read.StyleDescriptors?[0]);
        Assert.Single(read.ReferenceImages!);

        // New fields
        Assert.Equal(2, read.BackgroundPattern!.Count);
        Assert.Equal(2, read.TypicalSlideCount);
        Assert.Single(read.ColorPairings!);
        Assert.Equal("#C0FFEE", read.ColorPairings![0].BackgroundHex);
    }
}
