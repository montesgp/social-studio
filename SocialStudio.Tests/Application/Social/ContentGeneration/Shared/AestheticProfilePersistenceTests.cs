using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Persistence round-trip tests for <see cref="AestheticProfilePersistence"/>.
/// WU-1 (tasks 1.11–1.12): client-aligned-carousel-generation additive fields.
/// WU-2 (tasks 2.1–2.2): client-aesthetic-conceptual-detection additive fields (CACD-PA-05).
/// Refs: design §3.1/§7; CAC-BSP-02-b/c; CACD-PA-05-a/b/c.
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
    public void Serialize_ThenRead_MeasuredTypicalSlideCount_TakesPrecedenceOverPatternLength()
    {
        // measured-typical-slide-count: when the extractor MEASURED a typicalSlideCount (median across
        // posts), it must win over the BackgroundPattern-length proxy, even when they disagree.
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BackgroundPattern: [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.Photo],
            TypicalSlideCount: 5);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Equal(5, read.TypicalSlideCount); // measured value, NOT BackgroundPattern.Count (3)
    }

    [Fact]
    public void Serialize_ThenRead_NoMeasuredTypicalSlideCount_FallsBackToPatternLength()
    {
        // When the extractor did not measure typicalSlideCount (older extractor / model omitted it),
        // fall back to BackgroundPattern's length — same behaviour as before this field existed.
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BackgroundPattern: [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.Photo],
            TypicalSlideCount: null);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Equal(3, read.TypicalSlideCount);
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

    // ── WU-2 (CACD-PA-05): ConceptualMethod round-trip ───────────────────────

    /// <summary>
    /// CACD-PA-05-a — Serialize with a non-null ConceptualMethod, then Read returns the
    /// same enum token, rationale, and cues (task 2.1 RED / 2.2 GREEN).
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesConceptualMethod()
    {
        var method = new ConceptualMethod(
            Method: ArtDirectionMethod.MetaphoricalEditorial,
            Rationale: "message becomes visual metaphor",
            Cues: ["mono-color backdrop", "object-as-metaphor"]);

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            ConceptualMethod: method);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.MetaphoricalEditorial, read.ConceptualMethod!.Method);
        Assert.Equal("message becomes visual metaphor", read.ConceptualMethod.Rationale);
        Assert.Equal(2, read.ConceptualMethod.Cues.Count);
        Assert.Contains("mono-color backdrop", read.ConceptualMethod.Cues);
        Assert.Contains("object-as-metaphor", read.ConceptualMethod.Cues);
    }

    /// <summary>
    /// CACD-PA-05-a — Serialize with a non-null ImageTypeRatio, then Read returns the
    /// same proportions and sample size (task 2.1 RED / 2.2 GREEN).
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesImageTypeRatio()
    {
        var ratio = new ImageTypeRatio(Photo: 0.5, FlatColor: 0.4, Decorator: 0.1, SampleSize: 10);

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            ImageTypeRatio: ratio);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.ImageTypeRatio);
        Assert.Equal(0.5, read.ImageTypeRatio!.Photo);
        Assert.Equal(0.4, read.ImageTypeRatio.FlatColor);
        Assert.Equal(0.1, read.ImageTypeRatio.Decorator);
        Assert.Equal(10, read.ImageTypeRatio.SampleSize);
    }

    /// <summary>
    /// CACD-PA-05-b — A pre-v2 blob that has no conceptualMethod / imageTypeRatio keys
    /// must deserialize to null for both new fields without throwing (cold-start parity).
    /// Task 2.1 RED / 2.2 GREEN.
    /// </summary>
    [Fact]
    public void Read_PreV2Blob_ReturnsNull_ForConceptualMethodAndImageTypeRatio_NoException()
    {
        // JSON produced before CACD landed — no new keys present
        const string preV2Json =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[]}""";

        var read = AestheticProfilePersistence.Read(preV2Json);

        Assert.Null(read.ConceptualMethod);
        Assert.Null(read.ImageTypeRatio);
        // Existing fields still parse correctly
        Assert.NotNull(read.StyleDescriptors);
        Assert.Contains("editorial", read.StyleDescriptors!);
    }

    /// <summary>
    /// CACD-PA-05-c — Serialize/Read round-trip with both new fields null succeeds;
    /// both remain null (task 2.1 RED / 2.2 GREEN).
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_NullConceptualMethod_NullImageTypeRatio_RemainsNull()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null
            // ConceptualMethod = null (default), ImageTypeRatio = null (default)
        );

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Null(read.ConceptualMethod);
        Assert.Null(read.ImageTypeRatio);
    }

    // ── WU-4 (FFS-PA-02): BrandMark round-trip ───────────────────────────────

    /// <summary>
    /// FFS-PA-02-a — Serialize with a populated BrandMark, then Read returns the same
    /// Present/PositionHint/ColorHex with no data loss (task 2.6 RED / 2.7 GREEN).
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesBrandMark()
    {
        var brandMark = new BrandMarkDetection(Present: true, PositionHint: "bottom-right", ColorHex: "#112233");

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BrandMark: brandMark);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.BrandMark);
        Assert.True(read.BrandMark!.Present);
        Assert.Equal("bottom-right", read.BrandMark.PositionHint);
        Assert.Equal("#112233", read.BrandMark.ColorHex);
    }

    /// <summary>
    /// FFS-PA-02-a — a BrandMark with Present=false round-trips correctly (not fabricated
    /// as absent-key; the extractor's explicit "no mark" answer is preserved).
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesBrandMark_WhenPresentIsFalse()
    {
        var brandMark = new BrandMarkDetection(Present: false, PositionHint: null, ColorHex: null);

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BrandMark: brandMark);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.BrandMark);
        Assert.False(read.BrandMark!.Present);
    }

    /// <summary>
    /// FFS-PA-02-b — A pre-v3 blob with no brandMark key deserializes with BrandMark = null,
    /// no exception (cold-start parity; task 2.6 RED / 2.7 GREEN).
    /// </summary>
    [Fact]
    public void Read_PreV3Blob_ReturnsNull_ForBrandMark_NoException()
    {
        const string preV3Json =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[]}""";

        var read = AestheticProfilePersistence.Read(preV3Json);

        Assert.Null(read.BrandMark);
        Assert.NotNull(read.StyleDescriptors);
        Assert.Contains("editorial", read.StyleDescriptors!);
    }

    [Fact]
    public void Serialize_ThenRead_NullBrandMark_RemainsNull()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null
            // BrandMark = null (default)
        );

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Null(read.BrandMark);
    }

    // ── WU-6a (CAGH-PA-01/02): MarkType + Text round-trip ────────────────────

    /// <summary>
    /// FFS-PA-02-a — Serialize with a populated MarkType/Text, then Read returns both with no
    /// data loss (task 6a.4).
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesMarkTypeAndText()
    {
        var brandMark = new BrandMarkDetection(
            Present: true,
            PositionHint: "bottom-right",
            ColorHex: "#112233",
            MarkType: "text",
            Text: "Dra. Lucia Fernandez");

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BrandMark: brandMark);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.BrandMark);
        Assert.Equal("text", read.BrandMark!.MarkType);
        Assert.Equal("Dra. Lucia Fernandez", read.BrandMark.Text);
    }

    /// <summary>
    /// FFS-PA-02-c — a stored blob whose brandMark object carries only the pre-hardening
    /// present/positionHint/colorHex shape deserializes MarkType/Text as null, pre-existing fields
    /// intact, no exception (task 6a.4).
    /// </summary>
    [Fact]
    public void Read_PreV5BrandMarkShape_MarkTypeAndTextAreNull_PreExistingFieldsIntact_NoException()
    {
        const string preV5Json =
            """{"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,"referenceImages":[],"brandMark":{"present":true,"positionHint":"bottom-right","colorHex":"#112233"}}""";

        var read = AestheticProfilePersistence.Read(preV5Json);

        Assert.NotNull(read.BrandMark);
        Assert.True(read.BrandMark!.Present);
        Assert.Equal("bottom-right", read.BrandMark.PositionHint);
        Assert.Equal("#112233", read.BrandMark.ColorHex);
        Assert.Null(read.BrandMark.MarkType);
        Assert.Null(read.BrandMark.Text);
    }

    // ── aesthetic-freshness-versioning: SchemaVersion round-trips the ACTUAL extractor version ──

    /// <summary>
    /// Serialize MUST stamp the extraction's own <c>SchemaVersion</c> (the extractor that actually
    /// produced it), NOT a hardcoded constant — otherwise every stored profile would report the
    /// same version regardless of which extractor ran, permanently breaking schema-version
    /// comparisons in <c>AestheticFreshness.Evaluate</c>.
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_RoundTripsActualSchemaVersion()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            SchemaVersion: "aesthetic-extractor/v3");

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Equal("aesthetic-extractor/v3", read.SchemaVersion);
    }

    /// <summary>
    /// Serialize with a DIFFERENT SchemaVersion than the extractor's other calls stamps that exact
    /// value — proves the write path is NOT hardcoded to a single constant.
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_DifferentSchemaVersions_EachRoundTripsIndependently()
    {
        var v2Extraction = new AestheticExtraction([], [], [], null, SchemaVersion: "aesthetic-extractor/v2");
        var v3Extraction = new AestheticExtraction([], [], [], null, SchemaVersion: "aesthetic-extractor/v3");

        var v2Read = AestheticProfilePersistence.Read(AestheticProfilePersistence.Serialize(v2Extraction, []));
        var v3Read = AestheticProfilePersistence.Read(AestheticProfilePersistence.Serialize(v3Extraction, []));

        Assert.Equal("aesthetic-extractor/v2", v2Read.SchemaVersion);
        Assert.Equal("aesthetic-extractor/v3", v3Read.SchemaVersion);
    }

    /// <summary>
    /// When the extraction carries no SchemaVersion (e.g. <c>AestheticExtraction.Empty</c>),
    /// Serialize falls back to the pre-versioning default for safety — never null/omitted.
    /// </summary>
    [Fact]
    public void Serialize_NullSchemaVersion_FallsBackToDefault()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null
            // SchemaVersion = null (default)
        );

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Equal("aesthetic-extractor/v2", read.SchemaVersion);
    }

    /// <summary>
    /// An old blob with no <c>promptVersion</c> key at all (pre-versioning) deserializes with
    /// SchemaVersion = null, no exception (cold-start parity) — treated as the oldest version by
    /// <c>AestheticFreshness.ParseSchemaVersion</c>.
    /// </summary>
    [Fact]
    public void Read_OldBlobWithoutPromptVersionKey_ReturnsNullSchemaVersion_NoException()
    {
        const string oldJson =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[]}""";

        var read = AestheticProfilePersistence.Read(oldJson);

        Assert.Null(read.SchemaVersion);
    }

    // ── WU-2a (CAFF-PA-02): three-role Typography round-trip ─────────────────

    /// <summary>
    /// CAFF-PA-02 — Serialize with heading/body/caption Typography, then Read returns all
    /// three roles with no data loss.
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesThreeRoleTypography()
    {
        IReadOnlyList<AestheticTypography> typography =
        [
            new AestheticTypography(TypographyRoles.Heading, "Poppins", 700, false, 0.08),
            new AestheticTypography(TypographyRoles.Body, "Inter", 400, false, 0.032),
            new AestheticTypography("caption", "Poppins", 600, true, 0.02),
        ];

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            Typography: typography);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.Typography);
        Assert.Equal(3, read.Typography!.Count);
        var heading = read.Typography.Single(t => t.Role == TypographyRoles.Heading);
        Assert.Equal("Poppins", heading.FontFamily);
        Assert.Equal(700, heading.Weight);
        var body = read.Typography.Single(t => t.Role == TypographyRoles.Body);
        Assert.Equal("Inter", body.FontFamily);
        var caption = read.Typography.Single(t => t.Role == "caption");
        Assert.Equal("Poppins", caption.FontFamily);
        Assert.True(caption.Italic);
        Assert.Equal(0.02, caption.SizeRatio, precision: 10);
    }

    /// <summary>
    /// CAFF-PA-02 — a pre-change blob with only heading/body typography (no caption role, or
    /// no typography key at all) deserializes with caption absent from the list, no exception.
    /// </summary>
    [Fact]
    public void Read_PreChangeBlob_TwoRoleTypography_NoCaptionKey_ReadsWithoutCaption_NoException()
    {
        const string twoRoleJson =
            """{"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,"referenceImages":[],"typography":[{"role":"heading","fontFamily":"Poppins","weight":700,"italic":false,"sizeRatio":0.08},{"role":"body","fontFamily":"Inter","weight":400,"italic":false,"sizeRatio":0.032}]}""";

        var read = AestheticProfilePersistence.Read(twoRoleJson);

        Assert.NotNull(read.Typography);
        Assert.Equal(2, read.Typography!.Count);
        Assert.DoesNotContain(read.Typography, t => t.Role == "caption");
    }

    [Fact]
    public void Read_OldBlobWithoutTypographyKey_ReturnsNull_NoException()
    {
        const string oldJson =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[]}""";

        var read = AestheticProfilePersistence.Read(oldJson);

        Assert.Null(read.Typography);
        Assert.NotNull(read.StyleDescriptors);
        Assert.Contains("editorial", read.StyleDescriptors!);
    }

    // ── WU-3a (CAFF-PA-02/03): per-slide BackgroundTransparency round-trip ───

    /// <summary>
    /// CAFF-PA-03 — Serialize with a populated per-slide transparency signal, then Read returns the
    /// same boolean + descriptor values with no data loss, including a null (unmeasured) entry.
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesBackgroundTransparency()
    {
        IReadOnlyList<SlideBackgroundTransparency?> transparency =
        [
            new SlideBackgroundTransparency(false, null),
            new SlideBackgroundTransparency(true, "faded overlay"),
            null,
        ];

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BackgroundTransparency: transparency);

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.BackgroundTransparency);
        Assert.Equal(3, read.BackgroundTransparency!.Count);
        Assert.False(read.BackgroundTransparency[0]!.Blended);
        Assert.Null(read.BackgroundTransparency[0]!.Descriptor);
        Assert.True(read.BackgroundTransparency[1]!.Blended);
        Assert.Equal("faded overlay", read.BackgroundTransparency[1]!.Descriptor);
        Assert.Null(read.BackgroundTransparency[2]);
    }

    /// <summary>
    /// CAFF-PA-02 (persistence half) — a legacy blob without the <c>backgroundTransparency</c> key
    /// deserializes with the field null, no exception (cold-start parity).
    /// </summary>
    [Fact]
    public void Read_LegacyBlobWithoutBackgroundTransparencyKey_ReturnsNull_NoException()
    {
        const string legacyJson =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[]}""";

        var read = AestheticProfilePersistence.Read(legacyJson);

        Assert.Null(read.BackgroundTransparency);
        Assert.NotNull(read.StyleDescriptors);
        Assert.Contains("editorial", read.StyleDescriptors!);
    }

    [Fact]
    public void Serialize_ThenRead_NullBackgroundTransparency_RemainsNull()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null
            // BackgroundTransparency = null (default)
        );

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Null(read.BackgroundTransparency);
    }

    // ── client-analysis-data admin panel: WithBackgroundPattern patches ONLY the
    // BackgroundPattern (+ derived TypicalSlideCount) fields, round-tripping every other
    // stored field untouched (must-not-corrupt-other-fields constraint) ──────────────────

    [Fact]
    public void WithBackgroundPattern_PatchesOnlyBackgroundPatternAndTypicalSlideCount()
    {
        var extraction = new AestheticExtraction(
            Palette: [new AestheticPaletteEntry("#C0FFEE", "Primary", 1)],
            StyleDescriptors: ["airy"],
            Motifs: ["film grain"],
            CaptionVoice: "playful",
            BackgroundPattern: [SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor]);
        var existingJson = AestheticProfilePersistence.Serialize(extraction, []);

        var patchedJson = AestheticProfilePersistence.WithBackgroundPattern(
            existingJson,
            [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.Photo]);
        var read = AestheticProfilePersistence.Read(patchedJson);

        Assert.Equal(3, read.BackgroundPattern!.Count);
        Assert.Equal(SlideBackgroundKind.Photo, read.BackgroundPattern[0]);
        Assert.Equal(SlideBackgroundKind.FlatColor, read.BackgroundPattern[1]);
        Assert.Equal(SlideBackgroundKind.Photo, read.BackgroundPattern[2]);
        Assert.Equal(3, read.TypicalSlideCount);

        // Untouched fields preserved
        Assert.Equal("airy", read.StyleDescriptors?[0]);
        Assert.Equal("playful", read.CaptionVoice);
        Assert.Equal("#C0FFEE", read.Palette![0].Hex);
        Assert.Single(read.Motifs!);
    }

    [Fact]
    public void WithBackgroundPattern_NullExistingJson_CreatesNewPayload_WithOnlyBackgroundPatternSet()
    {
        var patchedJson = AestheticProfilePersistence.WithBackgroundPattern(
            null,
            [SlideBackgroundKind.Photo, SlideBackgroundKind.Photo]);
        var read = AestheticProfilePersistence.Read(patchedJson);

        Assert.Equal(2, read.BackgroundPattern!.Count);
        Assert.Equal(2, read.TypicalSlideCount);
        Assert.True(read.Palette is null or { Count: 0 });
    }

    [Fact]
    public void WithBackgroundPattern_MalformedExistingJson_CreatesNewPayload_NoException()
    {
        const string malformed = "{not valid json";

        var patchedJson = AestheticProfilePersistence.WithBackgroundPattern(
            malformed,
            [SlideBackgroundKind.FlatColor]);
        var read = AestheticProfilePersistence.Read(patchedJson);

        Assert.Single(read.BackgroundPattern!);
        Assert.Equal(SlideBackgroundKind.FlatColor, read.BackgroundPattern![0]);
        Assert.Equal(1, read.TypicalSlideCount);
    }

    // ── client-aesthetic-visual-sophistication (WU-3): DecorationMotifs round-trip ──────────

    /// <summary>
    /// CAVS-PA-02-b — an unrecognised composition-archetype token round-trips VERBATIM through
    /// Serialize/Read, never collapsed to a placeholder like "unknown".
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesUnrecognisedCompositionArchetypeVerbatim()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            DecorationMotifs: new DecorationMotifs(CompositionArchetype: "diagonal-split"));

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.DecorationMotifs);
        Assert.Equal("diagonal-split", read.DecorationMotifs!.CompositionArchetype);
    }

    /// <summary>
    /// CAVS-PA-03-a — a recognised divider-rule enum value round-trips intact.
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesDividerRule()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            DecorationMotifs: new DecorationMotifs(DividerRule: DividerRuleStyle.LineWithDiamond));

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.DecorationMotifs);
        Assert.Equal(DividerRuleStyle.LineWithDiamond, read.DecorationMotifs!.DividerRule);
    }

    /// <summary>
    /// CAVS-PA-04-a — a nested ornament detection (source path + bounding box) round-trips intact.
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_PreservesOrnamentDetection()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            DecorationMotifs: new DecorationMotifs(
                Ornament: new DecorativeOrnamentDetection(
                    SourceImagePath: "clients/abc/posts/1/image-0.jpg",
                    BoundingBox: new BrandMarkBoundingBox(0.1, 0.2, 0.3, 0.4))));

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.DecorationMotifs);
        Assert.NotNull(read.DecorationMotifs!.Ornament);
        var ornament = read.DecorationMotifs.Ornament!;
        Assert.Equal("clients/abc/posts/1/image-0.jpg", ornament.SourceImagePath);
        Assert.NotNull(ornament.BoundingBox);
        var box = ornament.BoundingBox!;
        Assert.Equal(0.1, box.X);
        Assert.Equal(0.2, box.Y);
        Assert.Equal(0.3, box.Width);
        Assert.Equal(0.4, box.Height);
    }

    /// <summary>CAVS-PA-01-a — no decoration signal ⇒ DecorationMotifs stays null, no exception.</summary>
    [Fact]
    public void Serialize_NullDecorationMotifs_RoundTripsNull()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null
            // DecorationMotifs = null (default)
        );

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var read = AestheticProfilePersistence.Read(json);

        Assert.Null(read.DecorationMotifs);
    }

    /// <summary>
    /// CAVS-PA-01-b — a pre-change blob (no <c>decorationMotifs</c> key) deserializes with
    /// <c>DecorationMotifs = null</c>, all pre-existing fields intact, no exception.
    /// </summary>
    [Fact]
    public void Read_PreV6BlobWithoutDecorationMotifsKey_ReturnsNullDecorationMotifs_PreExistingFieldsIntact_NoException()
    {
        const string preV6Json =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[]}""";

        var read = AestheticProfilePersistence.Read(preV6Json);

        Assert.Null(read.DecorationMotifs);
        Assert.Equal("playful", read.CaptionVoice);
        Assert.Single(read.StyleDescriptors!);
        Assert.Equal("editorial", read.StyleDescriptors![0]);
    }

    /// <summary>
    /// CAVS-PA-03-b — a divider-rule token that was somehow stored as an unrecognised string
    /// deserializes as <c>DividerRule = null</c>, never throws.
    /// </summary>
    [Fact]
    public void Read_UnrecognisedStoredDividerRuleToken_ReturnsNullDividerRule_NoException()
    {
        const string json =
            """{"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,"referenceImages":[],"decorationMotifs":{"dividerRule":"zigzag","compositionArchetype":"left-aligned"}}""";

        var read = AestheticProfilePersistence.Read(json);

        Assert.NotNull(read.DecorationMotifs);
        Assert.Null(read.DecorationMotifs!.DividerRule);
        Assert.Equal("left-aligned", read.DecorationMotifs.CompositionArchetype);
    }
}
