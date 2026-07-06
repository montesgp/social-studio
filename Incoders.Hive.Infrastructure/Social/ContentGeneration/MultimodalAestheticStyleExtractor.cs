using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// <see cref="IClientAestheticStyleExtractor"/> that learns a client's visual aesthetic and
/// conceptual art-direction method from their durably-hosted post images + captions using the org's
/// configured MULTIMODAL route (the optional <c>extract-aesthetic</c> / <c>ai-aesthetic</c> workflow
/// step). Phase 2 (CAG2-PA-03); extended in CACD with ConceptualMethod + ImageTypeRatio (v2).
/// </summary>
/// <remarks>
/// Reads each reference image's bytes from storage, base64-encodes them, and sends them as vision
/// input alongside a text instruction. Each image is tagged with the caption of its parent post when
/// available (CACD-PA-03; OQ-3). Palette is OPTIONAL output: an empty palette is a VALID result.
/// When no aesthetic route is configured, or no images resolve, returns
/// <see cref="AestheticExtraction.Empty"/> (the caller degrades to the Phase 1 text signal).
/// </remarks>
public sealed class MultimodalAestheticStyleExtractor : IClientAestheticStyleExtractor
{
    private const string CapabilityKey = "ai-aesthetic";
    // aesthetic-extractor/v3: adds brandMark {present,positionHint,colorHex} detection (FFS-PA-01).
    // Additive/backward-compatible: absent brandMark key on a v2 response still maps cleanly (null).
    // aesthetic-freshness-versioning: MUST stay equal to AestheticFreshness.CurrentSchemaVersion —
    // referencing the constant directly (both are compile-time consts) guarantees they never drift.
    private const string PromptVersion = AestheticFreshness.CurrentSchemaVersion;

    private readonly IAiCompletionGateway _gateway;
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<MultimodalAestheticStyleExtractor> _logger;

    public MultimodalAestheticStyleExtractor(
        IAiCompletionGateway gateway,
        IWorkflowConfigurationResolver workflowResolver,
        IGeneratedAssetStorage storage,
        ILogger<MultimodalAestheticStyleExtractor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(storage);
        _gateway = gateway;
        _workflowResolver = workflowResolver;
        _storage = storage;
        _logger = logger ?? NullLogger<MultimodalAestheticStyleExtractor>.Instance;
    }

    public async Task<AestheticExtraction> ExtractAsync(
        Guid organizationId,
        IReadOnlyList<ProfilePostImageCaptionPair> referenceImages,
        CancellationToken ct)
    {
        if (referenceImages.Count == 0)
        {
            return AestheticExtraction.Empty;
        }

        // OQ-6: Resolve the OPTIONAL aesthetic route. The "ai-aesthetic" capability key MUST be
        // configured to a real vision-capable API model — e.g., openai / gpt-4o or
        // anthropic / claude-3-5-sonnet via BYOK API key.
        // The claude-p CLI provider is explicitly EXCLUDED: it is a backend-local CLI agent that
        // refuses JSON contracts and cannot perform structured multimodal vision extraction.
        // Configured model key: resolved per-org via IWorkflowConfigurationResolver.
        // No route configured → skip extraction (degrade to Phase 1 text-signal path).
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                organizationId,
                "single-analyzer",
                "profile-analysis",
                "extract-aesthetic",
                "ai-aesthetic",
                null),
            ct);

        var candidate = route.IsSuccess
            ? route.Value.Where(c => c.IsEnabled).OrderBy(c => c.Priority).FirstOrDefault()
            : null;
        if (candidate is null)
        {
            _logger.LogInformation(
                "AestheticExtractor: no configured aesthetic (multimodal) route for org {OrganizationId} — skipping extraction.",
                organizationId);
            return AestheticExtraction.Empty;
        }

        // Load image bytes from durable storage and base64-encode for vision input.
        // Track loaded pairs so each image can be annotated with its post caption.
        var loadedPairs = new List<(AiImageInput Image, string? Caption)>(referenceImages.Count);
        foreach (var pair in referenceImages)
        {
            var opened = await _storage.OpenAsync(pair.Image.StoragePath, ct);
            if (opened.IsFailure)
            {
                _logger.LogWarning(
                    "AestheticExtractor: could not open reference image {Path} (org {OrganizationId}) — skipping it.",
                    pair.Image.StoragePath, organizationId);
                continue;
            }

            using var stream = opened.Value;
            using var buffer = new MemoryStream();
            await stream.Content.CopyToAsync(buffer, ct);
            loadedPairs.Add((new AiImageInput(stream.Mime, Convert.ToBase64String(buffer.ToArray())), pair.Caption));
        }

        if (loadedPairs.Count == 0)
        {
            return AestheticExtraction.Empty;
        }

        var images = loadedPairs.Select(lp => lp.Image).ToList();

        // Per-image caption annotations: each loaded image is tagged with the caption of its post
        // (CACD-PA-03; OQ-3). Caption = null → image-only marker (never throws).
        var captionAnnotations = loadedPairs
            .Select((lp, i) => lp.Caption is not null
                ? $"Image {i}: \"{lp.Caption}\""
                : $"Image {i}: (no caption)")
            .ToList();

        var captionBlock = "Post captions for each attached image (indexed left to right as attached):\n"
            + string.Join("\n", captionAnnotations);

        var instruction =
            "You are a brand-aesthetics analyst. The attached images are WHOLE recent carousels/posts of a " +
            "single Instagram client (multiple slides per post, in order). Study how the client composes a " +
            "post — clients often MIX a couple of creative/photographic slides with 2-3 flat colour/template " +
            "slides within the same carousel, and vary those templates across posts. Capture that COMBINATION " +
            "and recurring system, not just one image. Extract the client's VISUAL LANGUAGE so we can generate " +
            "NEW content that looks like THEY made it. Return: palette (role-ordered hex, ONLY if the client " +
            "clearly uses marked colours — otherwise return an EMPTY palette array, do not invent one), " +
            "styleDescriptors (composition, slide-mix pattern, layout system, mood, treatment), motifs " +
            "(recurring visual elements / template traits), and captionVoice (their copy tone). " +
            captionBlock + " " +
            // CACD-PA-01 (OQ-1): deterministic conceptual method elicitation.
            "Identify the client's ART-DIRECTION METHOD — how a MESSAGE becomes an IMAGE — and return " +
            "`method` inside a `conceptualMethod` object as EXACTLY ONE of: Unknown, MetaphoricalEditorial, " +
            "LiteralIllustration, SymbolicStillLife, DocumentaryPhotography, GraphicTypographic, Mixed. " +
            "Also return `rationale` (one sentence describing the client's message→visual relationship) " +
            "and `cues` (array of observable visual cues, e.g. \"mono-color saturated backdrop\"). " +
            // CACD-PA-03 (OQ-3): caption→visual relationship inference.
            "Each image is tagged above with the caption (message) of its post; use these captions to " +
            "infer HOW the client translates a message into a visual concept. " +
            // CACD-PA-02 (OQ-2): corpus-grounded image-type count.
            "Count, across ALL attached posts, how many slides are photographic (real photos or staged " +
            "photography), flat-color (solid background with template/text overlays), or decorator " +
            "(purely decorative / graphic dividers) and return `imageTypeCounts` " +
            "with fields `photo`, `flat`, and `decorator` (integer counts, not proportions). " +
            // FFS-PA-01: recurring brand-mark detection (v3). Only report a mark that RECURS
            // consistently across the attached images — never invent one from a single occurrence.
            "Detect whether the SAME identifying mark (logo, emblem, watermark, or signature glyph) " +
            "recurs consistently across the attached images. Return `brandMark` with `present` " +
            "(boolean — true ONLY if a mark clearly recurs; otherwise false, do not guess), " +
            "`positionHint` (its approximate on-slide position, e.g. \"bottom-right\", " +
            "\"bottom-left\", \"bottom-center\", \"top-right\", \"top-left\", \"top-center\", " +
            "\"center\" — null when present is false or the position is unclear), and `colorHex` " +
            "(the mark's color as a hex string — typically the client's text color — null when " +
            "present is false or no distinct color is identifiable).";

        var request = new AiJsonCompletionRequest(
            OrganizationId: organizationId,
            AnalysisId: null,
            CapabilityKey: CapabilityKey,
            PromptVersionKey: PromptVersion,
            InputJson: instruction,
            PreferredProviderKey: candidate.ProviderKey,
            PreferredModel: candidate.ModelId,
            Images: images);

        var result = await _gateway.CompleteJsonAsync<AestheticExtractionDto>(request, ct);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "AestheticExtractor: extraction call failed for org {OrganizationId}: {ErrorCode}. Degrading to no aesthetic.",
                organizationId, result.Error.Code);
            return AestheticExtraction.Empty;
        }

        return Map(result.Value.Payload);
    }

    // Default background-candidate roles used when calling ColorPairingDerivation from the extractor.
    // The extractor has no access to the brand manual, so it considers all non-Neutral roles as candidates.
    private static readonly IReadOnlyList<ColorRole> DefaultBackgroundRoles =
        [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent];

    private static AestheticExtraction Map(AestheticExtractionDto dto)
    {
        var palette = (dto.Palette ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Hex))
            .Select((p, i) => new AestheticPaletteEntry(p.Hex!, p.Role, p.Rank > 0 ? p.Rank : i + 1))
            .ToArray();

        // client-aligned-carousel-generation: map slideMixPattern → BackgroundPattern (CAC-BSP-01-a)
        var backgroundPattern = MapSlideMixPattern(dto.SlideMixPattern);

        // client-aligned-carousel-generation: derive color pairings from extracted palette (CAC-BSP-03)
        // Uses all non-Neutral roles as background candidates since the brand manual is not available here.
        var colorPairings = ColorPairingDerivation.Derive(DefaultBackgroundRoles, palette);

        // CACD-PA-01 (OQ-1): map conceptualMethod DTO → ConceptualMethod value object.
        // Unrecognised method token → Unknown (deterministic; no nullable suppressor).
        // Null or empty rationale → ""; null cues → [].
        ConceptualMethod? conceptualMethod = null;
        if (dto.ConceptualMethod is { Method: { } methodStr })
        {
            var method = Enum.TryParse<ArtDirectionMethod>(methodStr, ignoreCase: false, out var parsed)
                ? parsed
                : ArtDirectionMethod.Unknown;
            conceptualMethod = new ConceptualMethod(
                method,
                string.IsNullOrWhiteSpace(dto.ConceptualMethod.Rationale) ? string.Empty : dto.ConceptualMethod.Rationale,
                dto.ConceptualMethod.Cues ?? []);
        }

        // CACD-PA-02 (OQ-2): normalize imageTypeCounts → ImageTypeRatio (proportions; SampleSize).
        // Null counts or zero total → null ratio (cold-start parity).
        ImageTypeRatio? imageTypeRatio = null;
        if (dto.ImageTypeCounts is { } counts)
        {
            var sampleSize = counts.Photo + counts.Flat + counts.Decorator;
            if (sampleSize > 0)
            {
                imageTypeRatio = new ImageTypeRatio(
                    Photo: (double)counts.Photo / sampleSize,
                    FlatColor: (double)counts.Flat / sampleSize,
                    Decorator: (double)counts.Decorator / sampleSize,
                    SampleSize: sampleSize);
            }
        }

        // FFS-PA-01: map brandMark DTO → BrandMarkDetection VO. `Present: false` is a valid,
        // non-fabricated "no recurring mark" result — mapped whenever the model returns the
        // brandMark object at all. Absent brandMark key (older v2 responses) → null (cold-start
        // parity; FFS-CG-03-a).
        BrandMarkDetection? brandMark = dto.BrandMark is { } bm
            ? new BrandMarkDetection(bm.Present, bm.PositionHint, bm.ColorHex)
            : null;

        return new AestheticExtraction(
            Palette: palette,
            StyleDescriptors: dto.StyleDescriptors ?? [],
            Motifs: dto.Motifs ?? [],
            CaptionVoice: string.IsNullOrWhiteSpace(dto.CaptionVoice) ? null : dto.CaptionVoice,
            BackgroundPattern: backgroundPattern,
            ColorPairings: colorPairings.Count > 0 ? colorPairings : null,
            ConceptualMethod: conceptualMethod,
            ImageTypeRatio: imageTypeRatio,
            BrandMark: brandMark,
            // aesthetic-freshness-versioning: stamp the extractor's OWN version onto the extraction
            // so persistence stores the ACTUAL version that produced it, not a hardcoded constant.
            SchemaVersion: PromptVersion);
    }

    private static IReadOnlyList<SlideBackgroundKind>? MapSlideMixPattern(string[]? pattern)
    {
        if (pattern is null || pattern.Length == 0)
        {
            return null;
        }

        var result = new SlideBackgroundKind[pattern.Length];
        for (var i = 0; i < pattern.Length; i++)
        {
            result[i] = pattern[i].Trim().ToLowerInvariant() switch
            {
                "photo"       => SlideBackgroundKind.Photo,
                "flat"        => SlideBackgroundKind.FlatColor,
                "flatcolor"   => SlideBackgroundKind.FlatColor,
                "flat_color"  => SlideBackgroundKind.FlatColor,
                "decorator"   => SlideBackgroundKind.Decorator,
                _             => SlideBackgroundKind.FlatColor, // safe default for unknown values
            };
        }

        return result;
    }

    // -- AI response DTO (palette optional; slideMixPattern additive; CACD: conceptualMethod + imageTypeCounts additive) --
    private sealed record AestheticExtractionDto(
        [property: JsonPropertyName("palette")] PaletteEntryDto[]? Palette,
        [property: JsonPropertyName("styleDescriptors")] string[]? StyleDescriptors,
        [property: JsonPropertyName("motifs")] string[]? Motifs,
        [property: JsonPropertyName("captionVoice")] string? CaptionVoice,
        // client-aligned-carousel-generation: additive nullable field (CAC-BSP-01-a)
        [property: JsonPropertyName("slideMixPattern")] string[]? SlideMixPattern = null,
        // CACD-PA-01 (OQ-1): conceptual art-direction method (additive nullable; cold-start → null)
        [property: JsonPropertyName("conceptualMethod")] ConceptualMethodDto? ConceptualMethod = null,
        // CACD-PA-02 (OQ-2): aggregate image-type counts (additive nullable; cold-start → null)
        [property: JsonPropertyName("imageTypeCounts")] ImageTypeCountsDto? ImageTypeCounts = null,
        // FFS-PA-01: recurring brand mark (additive nullable; absent on pre-v3 responses → null)
        [property: JsonPropertyName("brandMark")] BrandMarkDto? BrandMark = null);

    private sealed record PaletteEntryDto(
        [property: JsonPropertyName("hex")] string? Hex,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("rank")] int Rank);

    // CACD-PA-01: conceptual method sub-DTO
    private sealed record ConceptualMethodDto(
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("rationale")] string? Rationale,
        [property: JsonPropertyName("cues")] string[]? Cues);

    // FFS-PA-01: brand-mark sub-DTO (present/positionHint/colorHex — v3)
    private sealed record BrandMarkDto(
        [property: JsonPropertyName("present")] bool Present,
        [property: JsonPropertyName("positionHint")] string? PositionHint,
        [property: JsonPropertyName("colorHex")] string? ColorHex);

    // CACD-PA-02: image-type counts sub-DTO (counts, not proportions — normalization is in Map)
    private sealed record ImageTypeCountsDto(
        [property: JsonPropertyName("photo")] int Photo,
        [property: JsonPropertyName("flat")] int Flat,
        [property: JsonPropertyName("decorator")] int Decorator);
}
