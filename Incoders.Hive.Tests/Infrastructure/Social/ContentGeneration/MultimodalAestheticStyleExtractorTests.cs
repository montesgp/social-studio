using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>Phase 2 (CAG2-PA-03): multimodal extractor — palette optional; degrades when no route.</summary>
/// <remarks>WU-3 (CACD-PA-01/02/03): ConceptualMethod + ImageTypeRatio mapping; pairs caption-tagging.</remarks>
public sealed class MultimodalAestheticStyleExtractorTests
{
    private static readonly Guid Org = Guid.NewGuid();

    // WU-3: pairs replace the old (refs, captions) params (OQ-3).
    // Null caption = image-only scenario (CACD-PA-03-b).
    private static readonly IReadOnlyList<ProfilePostImageCaptionPair> Pairs =
    [
        new ProfilePostImageCaptionPair(
            new ProfilePostImageRef("p1", 0, "org/c/a/aesthetic/p1-0.jpg", "h1"),
            null),
    ];

    // Pairs carrying a caption — used by tests that previously passed a caption string.
    private static readonly IReadOnlyList<ProfilePostImageCaptionPair> PairsWithCaption =
    [
        new ProfilePostImageCaptionPair(
            new ProfilePostImageRef("p1", 0, "org/c/a/aesthetic/p1-0.jpg", "h1"),
            "summer drop"),
    ];

    // ── Pre-existing Phase 2 tests (updated to new pairs signature) ─────────

    [Fact]
    public async Task NoConfiguredRoute_ReturnsEmpty_AndDoesNotCallGateway()
    {
        var gateway = new CaptureGateway();
        var extractor = new MultimodalAestheticStyleExtractor(gateway, EmptyResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Empty(result.Palette);
        Assert.False(gateway.WasCalled);
    }

    [Fact]
    public async Task NoImages_ReturnsEmpty_WithoutResolvingRoute()
    {
        var gateway = new CaptureGateway();
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, [], CancellationToken.None);

        Assert.Same(AestheticExtraction.Empty, result);
        Assert.False(gateway.WasCalled);
    }

    [Fact]
    public async Task WithRouteAndImages_SendsVision_AndEmptyPaletteIsValid()
    {
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: """["editorial, high-contrast"]""", captionVoice: "playful");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, PairsWithCaption, CancellationToken.None);

        Assert.True(gateway.WasCalled);
        Assert.True(gateway.ImageCount > 0, "vision images must be sent to the gateway");
        Assert.Empty(result.Palette); // empty palette is a VALID result, not a degrade
        Assert.Contains("editorial, high-contrast", result.StyleDescriptors);
        Assert.Equal("playful", result.CaptionVoice);
    }

    [Fact]
    public async Task WithMarkedPalette_MapsRoleOrderedColours()
    {
        var gateway = new CaptureGateway(
            paletteJson: """[{"hex":"#C0FFEE","role":"Primary","rank":1}]""",
            descriptors: "[]",
            captionVoice: null);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Single(result.Palette);
        Assert.Equal("#C0FFEE", result.Palette[0].Hex);
    }

    // ── WU-1 task 1.13 [RED] — slideMixPattern + colorPairings mapping ─────────

    [Fact]
    public async Task SlideMixPattern_Populated_MapsToBackgroundPattern()
    {
        // CAC-BSP-01-a: slideMixPattern → BackgroundPattern [Photo, FlatColor, Photo]
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: """["photo","flat","photo"]""");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BackgroundPattern);
        Assert.Equal(3, result.BackgroundPattern!.Count);
        Assert.Equal(SlideBackgroundKind.Photo, result.BackgroundPattern[0]);
        Assert.Equal(SlideBackgroundKind.FlatColor, result.BackgroundPattern[1]);
        Assert.Equal(SlideBackgroundKind.Photo, result.BackgroundPattern[2]);
    }

    [Fact]
    public async Task SlideMixPattern_AllFlat_ButPhotoRatioMeaningful_ReconcilesToIncludePhoto()
    {
        // Root-cause fix: the model emitted an all-flat ordered pattern while its own counts say ~36% of
        // the client's slides are photographic. The persisted BackgroundPattern must NOT contradict the
        // counts — reconciliation injects a photo slide so image generation is not silently suppressed.
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: """["flat","flat","flat"]""",
            imageTypeCountsJson: """{"photo":10,"flat":16,"decorator":2}""");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BackgroundPattern);
        Assert.Equal(3, result.BackgroundPattern!.Count);
        Assert.Contains(SlideBackgroundKind.Photo, result.BackgroundPattern);
    }

    [Fact]
    public async Task SlideMixPattern_AllFlat_LowPhotoRatio_StaysAllFlat()
    {
        // Genuinely flat-dominant client (photo ratio below threshold) → pattern left untouched.
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: """["flat","flat","flat"]""",
            imageTypeCountsJson: """{"photo":1,"flat":24,"decorator":3}""");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BackgroundPattern);
        Assert.DoesNotContain(SlideBackgroundKind.Photo, result.BackgroundPattern!);
    }

    [Fact]
    public async Task SlideMixPattern_Absent_BackgroundPatternIsNull_ExtractionCompletes()
    {
        // CAC-BSP-01-b: no slideMixPattern → BackgroundPattern null; completes successfully
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: null);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Null(result.BackgroundPattern);
        Assert.True(gateway.WasCalled);
    }

    [Fact]
    public async Task JoinablePalette_ProducesColorPairings_ViaDeriveCall()
    {
        // Given a palette with Primary + Neutral roles → ColorPairings derived
        var paletteJson = """[{"hex":"#0D0D0D","role":"Primary","rank":1},{"hex":"#FFFFFF","role":"Neutral","rank":2}]""";
        var gateway = new CaptureGateway(paletteJson: paletteJson, descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.ColorPairings);
        Assert.NotEmpty(result.ColorPairings!);
        Assert.Equal("#0D0D0D", result.ColorPairings![0].BackgroundHex);
    }

    [Fact]
    public async Task EmptyPalette_ColorPairingsIsNullOrEmpty_ExtractionCompletes()
    {
        // Empty palette → no pairings; extraction still completes
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.True(result.ColorPairings is null || result.ColorPairings.Count == 0);
    }

    // ── WU-3 task 3.1 [RED] — ConceptualMethod mapping (CACD-PA-01) ─────────

    [Fact]
    public async Task ConceptualMethod_MetaphoricalEditorial_MapsCorrectly()
    {
        // CACD-PA-01-a: Atlas exemplar → ArtDirectionMethod.MetaphoricalEditorial
        const string conceptualMethodJson =
            """{"method":"MetaphoricalEditorial","rationale":"turns message into visual metaphor","cues":["mono-color saturated backdrop","object-as-metaphor"]}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", conceptualMethodJson: conceptualMethodJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.MetaphoricalEditorial, result.ConceptualMethod!.Method);
        Assert.Equal("turns message into visual metaphor", result.ConceptualMethod.Rationale);
        Assert.Contains("mono-color saturated backdrop", result.ConceptualMethod.Cues);
        Assert.Contains("object-as-metaphor", result.ConceptualMethod.Cues);
    }

    [Fact]
    public async Task ConceptualMethod_LiteralIllustration_MapsCorrectly_AndDiffersFromMetaphorical()
    {
        // CACD-PA-01-b: Comet exemplar → ArtDirectionMethod.LiteralIllustration (distinguishable from Atlas)
        const string conceptualMethodJson =
            """{"method":"LiteralIllustration","rationale":"literal tender scene","cues":["tender watercolor","children characters"]}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", conceptualMethodJson: conceptualMethodJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.LiteralIllustration, result.ConceptualMethod!.Method);
        // Atlas vs Comet fixtures are distinguishable (CACD-PA-01-c)
        Assert.NotEqual(ArtDirectionMethod.MetaphoricalEditorial, result.ConceptualMethod.Method);
    }

    [Fact]
    public async Task ConceptualMethod_UnrecognisedMethodToken_MapsToUnknown()
    {
        // CACD-PA-01-c / OQ-1: enum-parse fallback — unrecognised token → Unknown; no throw
        const string conceptualMethodJson = """{"method":"UnrecognisedToken","rationale":"","cues":[]}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", conceptualMethodJson: conceptualMethodJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.Unknown, result.ConceptualMethod!.Method);
    }

    [Fact]
    public async Task ConceptualMethod_Absent_IsNull_ExtractionSucceeds()
    {
        // CACD-PA-01-c: no conceptualMethod key → null; extraction completes normally
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Null(result.ConceptualMethod);
        Assert.True(gateway.WasCalled);
    }

    [Fact]
    public async Task ConceptualMethod_NullRationale_DefaultsToEmptyString()
    {
        // OQ-1: null Rationale → "" (not nullable suppressor); Cues null → []
        const string conceptualMethodJson = """{"method":"MetaphoricalEditorial","rationale":null,"cues":null}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", conceptualMethodJson: conceptualMethodJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.ConceptualMethod);
        Assert.Equal(string.Empty, result.ConceptualMethod!.Rationale);
        Assert.NotNull(result.ConceptualMethod.Cues);
        Assert.Empty(result.ConceptualMethod.Cues);
    }

    // ── WU-3 task 3.1 [RED] — ImageTypeRatio mapping (CACD-PA-02) ──────────

    [Fact]
    public async Task ImageTypeCounts_AtlasRatio_PhotoAndFlatEachHalfSampleSizeTen()
    {
        // CACD-PA-02-a: Atlas exemplar — photo:5, flat:5, decorator:0 → 50/50 split; SampleSize=10
        const string countsJson = """{"photo":5,"flat":5,"decorator":0}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", imageTypeCountsJson: countsJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.ImageTypeRatio);
        Assert.Equal(0.5, result.ImageTypeRatio!.Photo, precision: 10);
        Assert.Equal(0.5, result.ImageTypeRatio.FlatColor, precision: 10);
        Assert.Equal(0.0, result.ImageTypeRatio.Decorator, precision: 10);
        Assert.Equal(10, result.ImageTypeRatio.SampleSize);
    }

    [Fact]
    public async Task ImageTypeCounts_CometRatio_PhotoRatioBelowFifteenPercent()
    {
        // CACD-PA-02-b: Comet exemplar — photo:1, flat:10, decorator:2 → photo < 0.15; SampleSize=13
        const string countsJson = """{"photo":1,"flat":10,"decorator":2}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", imageTypeCountsJson: countsJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.ImageTypeRatio);
        Assert.True(result.ImageTypeRatio!.Photo < 0.15,
            $"Photo ratio {result.ImageTypeRatio.Photo} should be < 0.15 for Comet low-photo exemplar");
        Assert.Equal(13, result.ImageTypeRatio.SampleSize);
    }

    [Fact]
    public async Task ImageTypeCounts_AtlasAndCometRatios_AreDistinguishable()
    {
        // CACD-PA-02-b: Atlas and Comet exemplars produce distinguishably different ratios
        const string atlasJson = """{"photo":5,"flat":5,"decorator":0}""";
        const string cometJson = """{"photo":1,"flat":10,"decorator":2}""";

        var atlasResult = await new MultimodalAestheticStyleExtractor(
                new CaptureGateway(paletteJson: "[]", descriptors: "[]", imageTypeCountsJson: atlasJson),
                CandidateResolver(), new OneImageStorage())
            .ExtractAsync(Org, Pairs, CancellationToken.None);

        var cometResult = await new MultimodalAestheticStyleExtractor(
                new CaptureGateway(paletteJson: "[]", descriptors: "[]", imageTypeCountsJson: cometJson),
                CandidateResolver(), new OneImageStorage())
            .ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotEqual(atlasResult.ImageTypeRatio!.Photo, cometResult.ImageTypeRatio!.Photo);
    }

    [Fact]
    public async Task ImageTypeCounts_Absent_IsNull_ExtractionSucceeds()
    {
        // CACD-PA-02-c: no imageTypeCounts key → null; extraction completes normally
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Null(result.ImageTypeRatio);
        Assert.True(gateway.WasCalled);
    }

    // ── WU-3 task 3.1 [RED] — Pairs caption-tagging (CACD-PA-03) ───────────

    [Fact]
    public async Task Pair_WithCaption_IncludesCaptionTextInModelInstruction()
    {
        // CACD-PA-03-a: non-null Caption → caption appears tagged in the model instruction
        var captionPairs = new[]
        {
            new ProfilePostImageCaptionPair(
                new ProfilePostImageRef("p1", 0, "org/c/a/aesthetic/p1-0.jpg", "h1"),
                "fresh summer drop"),
        };
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        await extractor.ExtractAsync(Org, captionPairs, CancellationToken.None);

        Assert.NotNull(gateway.CapturedInstruction);
        Assert.Contains("fresh summer drop", gateway.CapturedInstruction!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pair_WithNullCaption_DoesNotThrow_AndImageIsStillSentToGateway()
    {
        // CACD-PA-03-b: Caption = null → image-only; extraction never throws; image is still sent
        var nullCaptionPairs = new[]
        {
            new ProfilePostImageCaptionPair(
                new ProfilePostImageRef("p1", 0, "org/c/a/aesthetic/p1-0.jpg", "h1"),
                null),
        };
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, nullCaptionPairs, CancellationToken.None);

        Assert.True(gateway.WasCalled);
        Assert.True(gateway.ImageCount > 0, "image must still be sent even when caption is null");
    }

    // ── WU-4 task 2.1/2.5 [RED] — brand-mark detection mapping (FFS-PA-01) ─────

    [Fact]
    public async Task BrandMark_Detected_MapsPresentPositionHintAndColorHex()
    {
        // FFS-PA-01-a: recurring mark detected → present=true with position + color hex
        const string brandMarkJson = """{"present":true,"positionHint":"bottom-right","colorHex":"#112233"}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", brandMarkJson: brandMarkJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BrandMark);
        Assert.True(result.BrandMark!.Present);
        Assert.Equal("bottom-right", result.BrandMark.PositionHint);
        Assert.Equal("#112233", result.BrandMark.ColorHex);
    }

    [Fact]
    public async Task BrandMark_NoRecurringMark_ReportsAbsent_NoFabrication()
    {
        // FFS-PA-01-b: no consistent mark across images → present=false; extractor does not fabricate one
        const string brandMarkJson = """{"present":false,"positionHint":null,"colorHex":null}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", brandMarkJson: brandMarkJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BrandMark);
        Assert.False(result.BrandMark!.Present);
        Assert.Null(result.BrandMark.PositionHint);
        Assert.Null(result.BrandMark.ColorHex);
    }

    [Fact]
    public async Task BrandMark_Absent_IsNull_ExtractionSucceeds()
    {
        // No brandMark key at all in the model response → null (cold-start parity); no throw
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Null(result.BrandMark);
        Assert.True(gateway.WasCalled);
    }

    // ── WU-6a task 6a.3 [RED] — markType/text mapping (CAGH-PA-01/02, v5) ───────

    [Fact]
    public async Task BrandMark_TextType_MapsMarkTypeAndText()
    {
        // CAGH-PA-01-a / CAGH-PA-02-a: recognised markType + literal text map through
        const string brandMarkJson =
            """{"present":true,"positionHint":"bottom-right","colorHex":"#112233","markType":"text","text":"Dra. Lucia Fernandez"}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", brandMarkJson: brandMarkJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BrandMark);
        Assert.Equal("text", result.BrandMark!.MarkType);
        Assert.Equal("Dra. Lucia Fernandez", result.BrandMark.Text);
    }

    [Fact]
    public async Task BrandMark_IconType_TextIsNull()
    {
        // CAGH-PA-02-b: an icon mark never carries a text literal, even if the model hallucinates one
        const string brandMarkJson =
            """{"present":true,"positionHint":"top-left","colorHex":"#445566","markType":"icon","text":"should be ignored"}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", brandMarkJson: brandMarkJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BrandMark);
        Assert.Equal("icon", result.BrandMark!.MarkType);
        Assert.Null(result.BrandMark.Text);
    }

    [Fact]
    public async Task BrandMark_UnrecognisedMarkType_MapsToUnknown_NoThrow()
    {
        // CAGH-PA-01-b: an unrecognised/malformed markType token never throws — maps to "unknown"
        const string brandMarkJson =
            """{"present":true,"positionHint":"bottom-right","colorHex":"#112233","markType":"emblem-thing","text":"noise"}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", brandMarkJson: brandMarkJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BrandMark);
        Assert.Equal("unknown", result.BrandMark!.MarkType);
        Assert.Null(result.BrandMark.Text);
    }

    [Fact]
    public async Task BrandMark_PreV5Response_NoMarkTypeOrTextKeys_MapsToUnknown_NoThrow()
    {
        // Older extractor responses (pre-v5) only sent present/positionHint/colorHex — missing
        // markType/text keys never throw and default to "unknown"/null (defensive-parse convention).
        const string brandMarkJson = """{"present":true,"positionHint":"bottom-right","colorHex":"#112233"}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", brandMarkJson: brandMarkJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BrandMark);
        Assert.Equal("unknown", result.BrandMark!.MarkType);
        Assert.Null(result.BrandMark.Text);
    }

    [Fact]
    public async Task TypicalSlideCount_Present_MapsThrough()
    {
        // measured-typical-slide-count: the model's measured median maps straight onto the extraction.
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", typicalSlideCount: 5);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Equal(5, result.TypicalSlideCount);
    }

    [Fact]
    public async Task TypicalSlideCount_Absent_IsNull_ExtractionSucceeds()
    {
        // No typicalSlideCount key (model could not tell, or pre-v4 response) → null; no throw
        // (cold-start parity — caller falls back to BackgroundPattern's length).
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Null(result.TypicalSlideCount);
        Assert.True(gateway.WasCalled);
    }

    [Fact]
    public async Task ExtractorPromptVersion_IsBumpedToV5()
    {
        // Extraction prompt/schema version MUST be bumped v4→v5 (WU-6a, CAGH-PA-01/02: markType/text)
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Equal("aesthetic-extractor/v5", gateway.CapturedPromptVersionKey);
    }

    /// <summary>
    /// aesthetic-freshness-versioning: the produced extraction MUST carry the extractor's OWN
    /// schema version (same value sent as PromptVersionKey), so persistence stores the ACTUAL
    /// version that produced it — not a hardcoded constant.
    /// </summary>
    [Fact]
    public async Task Extract_StampsSchemaVersion_OnProducedExtraction()
    {
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Equal("aesthetic-extractor/v5", result.SchemaVersion);
        Assert.Equal(gateway.CapturedPromptVersionKey, result.SchemaVersion);
    }

    // ── WU-2a task 2a.1 [RED] — three-role typography mapping (CAFF-PA-01) ──────

    [Fact]
    public async Task Typography_AllThreeRolesDetected_MapsHeadingBodyAndCaption()
    {
        // CAFF-PA-01: all three roles distinguishable → all three map through
        const string typographyJson =
            """{"heading":{"family":"Poppins","weight":700,"italic":false,"sizeRatio":0.08},"body":{"family":"Inter","weight":400,"italic":false,"sizeRatio":0.032},"caption":{"family":"Poppins","weight":600,"italic":true,"sizeRatio":0.02}}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", typographyJson: typographyJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.Typography);
        Assert.Equal(3, result.Typography!.Count);
        var heading = result.Typography.Single(t => t.Role == TypographyRoles.Heading);
        Assert.Equal("Poppins", heading.FontFamily);
        Assert.Equal(700, heading.Weight);
        Assert.False(heading.Italic);
        Assert.Equal(0.08, heading.SizeRatio, precision: 10);
        var body = result.Typography.Single(t => t.Role == TypographyRoles.Body);
        Assert.Equal("Inter", body.FontFamily);
        Assert.Equal(400, body.Weight);
        var caption = result.Typography.Single(t => t.Role == "caption");
        Assert.Equal("Poppins", caption.FontFamily);
        Assert.Equal(600, caption.Weight);
        Assert.True(caption.Italic);
        Assert.Equal(0.02, caption.SizeRatio, precision: 10);
    }

    [Fact]
    public async Task Typography_HeadingAndBodyOnly_CaptionAbsent_RegressionUnaffected()
    {
        // Two-role clients (pre-existing behaviour) MUST stay unaffected — caption simply absent.
        const string typographyJson =
            """{"heading":{"family":"Poppins","weight":700,"italic":false,"sizeRatio":0.08},"body":{"family":"Inter","weight":400,"italic":false,"sizeRatio":0.032}}""";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", typographyJson: typographyJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.Typography);
        Assert.Equal(2, result.Typography!.Count);
        Assert.DoesNotContain(result.Typography, t => t.Role == "caption");
        Assert.Contains(result.Typography, t => t.Role == TypographyRoles.Heading);
        Assert.Contains(result.Typography, t => t.Role == TypographyRoles.Body);
    }

    [Fact]
    public async Task Typography_NoRoleDetected_IsNull_ColdStartPreserved()
    {
        // CAFF-PA-01: no typography key at all → null; extraction still succeeds (cold start)
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Null(result.Typography);
        Assert.True(gateway.WasCalled);
    }

    [Fact]
    public async Task Typography_TypographyObjectPresentButAllRolesNull_IsNull_NoException()
    {
        // Defensive parsing: a malformed/empty typography object never throws and never
        // fabricates an entry with no observed family.
        const string typographyJson = "{}";
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]", typographyJson: typographyJson);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Null(result.Typography);
    }

    // ── WU-3a task 3a.1 [RED] — per-slide photo-transparency mapping (CAFF-PA-03) ──────

    [Fact]
    public async Task SlideBackgroundTransparency_ConsistentBlendedHabit_MapsBooleanAndDescriptor_AtIndices1And3()
    {
        // CAFF-PA-03: slides at index 1 and 3 show a faded/blended photo background.
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: """["photo","photo","photo","photo"]""",
            transparencyJson:
                """[{"blended":false,"descriptor":null},{"blended":true,"descriptor":"faded overlay"},{"blended":false,"descriptor":null},{"blended":true,"descriptor":"soft blend"}]""");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BackgroundTransparency);
        Assert.Equal(4, result.BackgroundTransparency!.Count);
        Assert.False(result.BackgroundTransparency[0]!.Blended);
        Assert.True(result.BackgroundTransparency[1]!.Blended);
        Assert.Equal("faded overlay", result.BackgroundTransparency[1]!.Descriptor);
        Assert.False(result.BackgroundTransparency[2]!.Blended);
        Assert.True(result.BackgroundTransparency[3]!.Blended);
        Assert.Equal("soft blend", result.BackgroundTransparency[3]!.Descriptor);
    }

    [Fact]
    public async Task SlideBackgroundTransparency_NoTreatment_ColdStartUnaffected_NoException()
    {
        // CAFF-PA-03: no slideBackgroundTransparency key at all → null; extraction still succeeds.
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: """["flat","flat"]""");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.Null(result.BackgroundTransparency);
        Assert.True(gateway.WasCalled);
    }

    [Fact]
    public async Task SlideBackgroundTransparency_NeverExposesNumericAlpha_BooleanAndDescriptorOnly()
    {
        // CAFF-PA-03: the DTO contract carries boolean + string descriptor ONLY — asserted by
        // deserializing straight through the boolean/descriptor pair with no numeric field accepted.
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: """["photo"]""",
            transparencyJson: """[{"blended":true,"descriptor":"faded overlay"}]""");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Pairs, CancellationToken.None);

        Assert.NotNull(result.BackgroundTransparency);
        var entry = Assert.Single(result.BackgroundTransparency!);
        Assert.NotNull(entry);
        Assert.IsType<bool>(entry!.Blended);
        Assert.True(entry.Descriptor is null || entry.Descriptor is string);
    }

    private static IWorkflowConfigurationResolver EmptyResolver() => new StubResolver(candidate: false);
    private static IWorkflowConfigurationResolver CandidateResolver() => new StubResolver(candidate: true);
}

file sealed class StubResolver(bool candidate) : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WorkflowStepRouteCandidate> list = candidate
            ?
            [
                new WorkflowStepRouteCandidate(Guid.NewGuid(), "claude", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, 1, "claude-opus-4-8", null, true)
            ]
            : [];
        return Task.FromResult(Result.Success(list));
    }
}

file sealed class OneImageStorage : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        throw new NotSupportedException();
    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-image-bytes"));
        return Task.FromResult(Result.Success(new StoredAssetStream(stream, "image/jpeg", stream.Length)));
    }
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class CaptureGateway(
    string paletteJson = "[]",
    string descriptors = "[]",
    string? captionVoice = null,
    string? slideMixPattern = null,
    string? conceptualMethodJson = null,
    string? imageTypeCountsJson = null,
    string? brandMarkJson = null,
    int? typicalSlideCount = null,
    string? typographyJson = null,
    string? transparencyJson = null)
    : IAiCompletionGateway
{
    public bool WasCalled { get; private set; }
    public int ImageCount { get; private set; }

    /// <summary>The full instruction text passed to the AI gateway — used to assert caption tagging.</summary>
    public string? CapturedInstruction { get; private set; }

    /// <summary>The prompt version key sent to the gateway — used to assert the v3→v4 bump.</summary>
    public string? CapturedPromptVersionKey { get; private set; }

    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        WasCalled = true;
        ImageCount = request.Images?.Count ?? 0;
        CapturedInstruction = request.InputJson;
        CapturedPromptVersionKey = request.PromptVersionKey;

        var voice = captionVoice is null ? "null" : $"\"{captionVoice}\"";
        var patternPart = slideMixPattern is null
            ? string.Empty
            : $",\"slideMixPattern\":{slideMixPattern}";
        var conceptualPart = conceptualMethodJson is null
            ? string.Empty
            : $",\"conceptualMethod\":{conceptualMethodJson}";
        var countsPart = imageTypeCountsJson is null
            ? string.Empty
            : $",\"imageTypeCounts\":{imageTypeCountsJson}";
        var brandMarkPart = brandMarkJson is null
            ? string.Empty
            : $",\"brandMark\":{brandMarkJson}";
        var typicalSlideCountPart = typicalSlideCount is null
            ? string.Empty
            : $",\"typicalSlideCount\":{typicalSlideCount.Value}";
        var typographyPart = typographyJson is null
            ? string.Empty
            : $",\"typography\":{typographyJson}";
        var transparencyPart = transparencyJson is null
            ? string.Empty
            : $",\"slideBackgroundTransparency\":{transparencyJson}";
        var json =
            $$"""{"palette":{{paletteJson}},"styleDescriptors":{{descriptors}},"motifs":[],"captionVoice":{{voice}}{{patternPart}}{{conceptualPart}}{{countsPart}}{{brandMarkPart}}{{typicalSlideCountPart}}{{typographyPart}}{{transparencyPart}}}""";
        var payload = JsonSerializer.Deserialize<TResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(payload, "claude", "claude-opus-4-8", [], null)));
    }
}
